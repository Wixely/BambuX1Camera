using System.Net.Security;
using Avalonia.Controls;

namespace BambuLab.X1Camera.Desktop;

internal sealed class MainWindow : Window
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConnectionView connectionView = new();
    private X1CameraSession? session;
    private ViewerView? viewer;
    private DesktopSettings? loadedSettings;
    private PendingConnection? pendingConnection;

    public MainWindow()
    {
        Title = "Bambu X1 Camera";
        Width = 960;
        Height = 680;
        MinWidth = 620;
        MinHeight = 480;
        Content = connectionView;

        connectionView.ConnectRequested += (_, request) => _ = BeginConnectAsync(request);
        connectionView.TrustRequested += (_, _) => _ = TrustAndConnectAsync();
        Opened += (_, _) => _ = LoadSettingsAsync();
    }

    protected override void OnClosed(EventArgs eventArgs)
    {
        lifetime.Cancel();
        if (viewer is not null)
        {
            viewer.Stop();
            viewer.DisposeAsync().AsTask().GetAwaiter().GetResult();
            viewer = null;
        }

        if (session is not null)
        {
            session.DisposeAsync().AsTask().GetAwaiter().GetResult();
            session = null;
        }

        lifetime.Dispose();
        base.OnClosed(eventArgs);
    }

    private async Task LoadSettingsAsync()
    {
        var path = DesktopSettingsStore.DefaultPath;
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            loadedSettings = await DesktopSettingsStore.LoadAsync(path, lifetime.Token);
            connectionView.LoadSettings(loadedSettings);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            connectionView.ShowError("The saved settings file could not be read. Enter the connection details again.");
        }
    }

    private async Task BeginConnectAsync(ConnectionRequestedEventArgs request)
    {
        if (string.IsNullOrWhiteSpace(request.Host) || string.IsNullOrWhiteSpace(request.AccessCode))
        {
            connectionView.ShowError("Enter both the printer address and LAN access code.");
            return;
        }

        connectionView.HideCertificate();
        connectionView.SetBusy(true, "Checking the printer certificate…");
        try
        {
            var observation = await CertificatePinDiscovery.ObserveAsync(request.Host, 322, lifetime.Token);
            var savedPin = loadedSettings is { } saved
                && string.Equals(saved.Host, request.Host, StringComparison.OrdinalIgnoreCase)
                ? saved.CertificateSha256
                : null;

            if (observation.PolicyErrors == SslPolicyErrors.None)
            {
                await ConnectAsync(request, certificatePin: null);
                return;
            }

            if (string.Equals(savedPin, observation.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                await ConnectAsync(request, observation.Sha256);
                return;
            }

            pendingConnection = new PendingConnection(request, observation.Sha256);
            connectionView.SetBusy(false);
            connectionView.ShowCertificate(observation.Sha256);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            connectionView.SetBusy(false);
            connectionView.ShowError($"Could not reach the printer: {exception.Message}");
        }
    }

    private async Task TrustAndConnectAsync()
    {
        if (pendingConnection is not { } pending)
        {
            return;
        }

        pendingConnection = null;
        connectionView.SetBusy(true, "Connecting to the camera…");
        await ConnectAsync(pending.Request, pending.CertificateSha256);
    }

    private async Task ConnectAsync(ConnectionRequestedEventArgs request, string? certificatePin)
    {
        connectionView.SetBusy(true, "Connecting to the camera…");
        try
        {
            var options = new X1CameraOptions(request.Host, request.AccessCode)
            {
                PinnedCertificateSha256 = certificatePin
            };
            session = await new X1CameraClient().ConnectAsync(options, lifetime.Token);

            if (request.SaveSettings)
            {
                loadedSettings = new DesktopSettings(request.Host, request.AccessCode, certificatePin, 322);
                await DesktopSettingsStore.SaveAsync(DesktopSettingsStore.DefaultPath, loadedSettings, lifetime.Token);
            }

            viewer = new ViewerView(request.Host, session);
            viewer.DisconnectRequested += (_, _) => _ = DisconnectAsync();
            Content = viewer;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (session is not null)
            {
                await session.DisposeAsync();
                session = null;
            }

            connectionView.SetBusy(false);
            connectionView.ShowError($"Camera connection failed: {exception.Message}");
        }
    }

    private async Task DisconnectAsync()
    {
        if (viewer is not null)
        {
            await viewer.DisposeAsync();
            viewer = null;
        }

        if (session is not null)
        {
            await session.DisposeAsync();
            session = null;
        }

        pendingConnection = null;
        connectionView.SetBusy(false);
        connectionView.HideCertificate();
        Content = connectionView;
    }

    private sealed record PendingConnection(ConnectionRequestedEventArgs Request, string CertificateSha256);
}
