using System.Threading.Channels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BambuLab.X1Camera.Imaging;

namespace BambuLab.X1Camera.Desktop;

internal sealed class ViewerView : UserControl, IAsyncDisposable
{
    private const int MaximumGopAccessUnits = 300;
    private const int MaximumGopBytes = 32 * 1024 * 1024;
    private readonly CancellationTokenSource lifetime = new();
    private readonly X1CameraSession session;
    private readonly X1CameraAccessUnitHub hub;
    private readonly X1CameraAccessUnitSubscription videoSubscription;
    private readonly X1CameraAccessUnitSubscription stillSubscription;
    private readonly Channel<IReadOnlyList<H264AccessUnit>> encodedGops;
    private readonly Channel<IReadOnlyList<X1CameraDecodedFrame>> decodedGops;
    private readonly Image videoImage;
    private readonly Button captureButton;
    private readonly TextBlock statusText;
    private readonly Task collectorTask;
    private readonly Task decoderTask;
    private readonly Task playbackTask;
    private WriteableBitmap? displayedBitmap;
    private int disposed;

    public ViewerView(string host, X1CameraSession session)
    {
        this.session = session;
        hub = new X1CameraAccessUnitHub(session);
        videoSubscription = hub.Subscribe(capacity: 32);
        stillSubscription = hub.Subscribe(capacity: 2);
        encodedGops = Channel.CreateBounded<IReadOnlyList<H264AccessUnit>>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
        decodedGops = Channel.CreateBounded<IReadOnlyList<X1CameraDecodedFrame>>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });

        videoImage = new Image { Stretch = Stretch.Uniform };
        captureButton = new Button { Content = "Capture PNG" };
        var disconnectButton = new Button { Content = "Disconnect" };
        statusText = new TextBlock
        {
            Text = "Waiting for the first complete keyframe…",
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap
        };
        captureButton.Click += async (_, _) => await CapturePngAsync();
        disconnectButton.Click += (_, _) => DisconnectRequested?.Invoke(this, EventArgs.Empty);

        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Children =
            {
                new Border
                {
                    Padding = new Thickness(20, 14),
                    BorderBrush = Brushes.DimGray,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Child = new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                        Children =
                        {
                            new StackPanel
                            {
                                Spacing = 2,
                                Children =
                                {
                                    new TextBlock
                                    {
                                        Text = "Bambu X1 Camera",
                                        FontSize = 20,
                                        FontWeight = FontWeight.SemiBold
                                    },
                                    new TextBlock { Text = $"Connected to {host}", Foreground = Brushes.Gray }
                                }
                            },
                            new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                Spacing = 10,
                                Children = { captureButton, disconnectButton }
                            }.WithGridColumn(1)
                        }
                    }
                },
                new Border
                {
                    Margin = new Thickness(24),
                    Background = Brushes.Black,
                    CornerRadius = new CornerRadius(8),
                    ClipToBounds = true,
                    Child = videoImage
                }.WithGridRow(1),
                new Border
                {
                    Padding = new Thickness(24, 0, 24, 18),
                    Child = statusText
                }.WithGridRow(2)
            }
        };

        collectorTask = CollectGopsAsync();
        decoderTask = DecodeGopsAsync();
        playbackTask = PlayFramesAsync();
    }

    public event EventHandler? DisconnectRequested;

    public void Stop() => lifetime.Cancel();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        lifetime.Cancel();
        await videoSubscription.DisposeAsync();
        await stillSubscription.DisposeAsync();
        try
        {
            await Task.WhenAll(collectorTask, decoderTask, playbackTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }

        await hub.DisposeAsync();
        displayedBitmap?.Dispose();
        displayedBitmap = null;
        lifetime.Dispose();
    }

    private async Task CollectGopsAsync()
    {
        List<H264AccessUnit> current = [];
        var encodedBytes = 0;
        try
        {
            await foreach (var accessUnit in videoSubscription.ReadAllAsync(lifetime.Token).ConfigureAwait(false))
            {
                if (accessUnit.Discontinuity)
                {
                    current.Clear();
                    encodedBytes = 0;
                }

                if (accessUnit.IsKeyFrame)
                {
                    if (current.Count > 0)
                    {
                        encodedGops.Writer.TryWrite(current.ToArray());
                    }

                    current.Clear();
                    encodedBytes = 0;
                }

                if (current.Count == 0 && !accessUnit.IsKeyFrame)
                {
                    continue;
                }

                current.Add(accessUnit);
                encodedBytes += accessUnit.AnnexBData.Length;
                if (current.Count > MaximumGopAccessUnits || encodedBytes > MaximumGopBytes)
                {
                    current.Clear();
                    encodedBytes = 0;
                    PostStatus("A GOP exceeded the viewer limit; waiting for the next keyframe.", error: true);
                }
            }

            if (current.Count > 0)
            {
                encodedGops.Writer.TryWrite(current.ToArray());
            }
        }
        finally
        {
            encodedGops.Writer.TryComplete();
        }
    }

    private async Task DecodeGopsAsync()
    {
        var decoder = new X1CameraGopDecoder();
        try
        {
            await foreach (var gop in encodedGops.Reader.ReadAllAsync(lifetime.Token).ConfigureAwait(false))
            {
                try
                {
                    var frames = await Task.Run(() => decoder.Decode(gop), lifetime.Token).ConfigureAwait(false);
                    decodedGops.Writer.TryWrite(frames);
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    PostStatus("A video GOP could not be decoded; waiting for the next keyframe.", error: true);
                }
            }
        }
        finally
        {
            decodedGops.Writer.TryComplete();
        }
    }

    private async Task PlayFramesAsync()
    {
        IReadOnlyList<X1CameraDecodedFrame>? current = null;
        var index = 0;
        while (current is not null || await decodedGops.Reader.WaitToReadAsync(lifetime.Token).ConfigureAwait(false))
        {
            while (decodedGops.Reader.TryRead(out var newest))
            {
                current = newest;
                index = 0;
            }

            if (current is null || index >= current.Count)
            {
                current = null;
                continue;
            }

            var frame = current[index];
            var dropped = videoSubscription.DroppedAccessUnits;
            var metrics = session.Metrics;
            Dispatcher.UIThread.Post(() => ShowFrame(frame, dropped, metrics.Reconnects));

            var delay = index + 1 < current.Count
                ? current[index + 1].PresentationTime - frame.PresentationTime
                : TimeSpan.FromMilliseconds(40);
            delay = delay < TimeSpan.FromMilliseconds(5) || delay > TimeSpan.FromMilliseconds(100)
                ? TimeSpan.FromMilliseconds(40)
                : delay;
            index++;
            await Task.Delay(delay, lifetime.Token).ConfigureAwait(false);
            if (index >= current.Count)
            {
                current = null;
            }
        }
    }

    private async Task CapturePngAsync()
    {
        captureButton.IsEnabled = false;
        statusText.Text = "Waiting for a complete keyframe for PNG capture…";
        statusText.Foreground = Brushes.Gray;
        try
        {
            var image = await stillSubscription.CapturePngAsync(lifetime.Token);
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.StorageProvider is not { } storageProvider)
            {
                throw new InvalidOperationException("A platform file picker is not available.");
            }

            var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save camera still",
                SuggestedFileName = $"bambu-x1-{DateTime.Now:yyyyMMdd-HHmmss}.png",
                DefaultExtension = "png",
                FileTypeChoices =
                [
                    new FilePickerFileType("PNG image")
                    {
                        Patterns = ["*.png"],
                        MimeTypes = ["image/png"]
                    }
                ]
            });
            if (file is null)
            {
                statusText.Text = "PNG capture cancelled.";
                return;
            }

            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await stream.WriteAsync(image.Data, lifetime.Token);
            await stream.FlushAsync(lifetime.Token);
            statusText.Text = $"Saved a {image.Width}×{image.Height} PNG.";
            statusText.Foreground = Brushes.Gray;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            statusText.Text = $"PNG capture failed: {exception.Message}";
            statusText.Foreground = Brushes.IndianRed;
        }
        finally
        {
            captureButton.IsEnabled = !lifetime.IsCancellationRequested;
        }
    }

    private unsafe void ShowFrame(X1CameraDecodedFrame frame, long droppedAccessUnits, long reconnects)
    {
        if (lifetime.IsCancellationRequested)
        {
            return;
        }

        var bitmap = new WriteableBitmap(
            new PixelSize(frame.Width, frame.Height),
            new Vector(96, 96),
            PixelFormats.Rgb24,
            AlphaFormat.Opaque);
        using (var framebuffer = bitmap.Lock())
        {
            var sourceStride = checked(frame.Width * 3);
            fixed (byte* source = frame.Rgb24Data.Span)
            {
                for (var row = 0; row < frame.Height; row++)
                {
                    var destination = (byte*)framebuffer.Address + checked(row * framebuffer.RowBytes);
                    Buffer.MemoryCopy(
                        source + (row * sourceStride),
                        destination,
                        framebuffer.RowBytes,
                        sourceStride);
                }
            }
        }

        videoImage.Source = bitmap;
        displayedBitmap?.Dispose();
        displayedBitmap = bitmap;
        statusText.Text = $"{frame.Width}×{frame.Height} RGB24  •  viewer drops {droppedAccessUnits}  •  reconnects {reconnects}";
        statusText.Foreground = Brushes.Gray;
    }

    private void PostStatus(string message, bool error)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (lifetime.IsCancellationRequested)
            {
                return;
            }

            statusText.Text = message;
            statusText.Foreground = error ? Brushes.IndianRed : Brushes.Gray;
        });
    }
}

internal static class GridExtensions
{
    public static T WithGridRow<T>(this T control, int row)
        where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }

    public static T WithGridColumn<T>(this T control, int column)
        where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }
}
