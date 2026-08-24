using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace BambuLab.X1Camera.Desktop;

internal sealed class ViewerView : UserControl
{
    public ViewerView(string host)
    {
        var disconnectButton = new Button
        {
            Content = "Disconnect",
            HorizontalAlignment = HorizontalAlignment.Right
        };
        disconnectButton.Click += (_, _) => DisconnectRequested?.Invoke(this, EventArgs.Empty);

        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
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
                            disconnectButton.WithGridColumn(1)
                        }
                    }
                },
                new Border
                {
                    Margin = new Thickness(24),
                    Background = Brushes.Black,
                    CornerRadius = new CornerRadius(8),
                    Child = new TextBlock
                    {
                        Text = "Connected. Live video is the next feature.",
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextWrapping = TextWrapping.Wrap
                    }
                }.WithGridRow(1)
            }
        };
    }

    public event EventHandler? DisconnectRequested;
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
