using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace BambuLab.X1Camera.Desktop;

internal sealed class ConnectionView : UserControl
{
    private readonly TextBox hostInput;
    private readonly TextBox accessCodeInput;
    private readonly CheckBox saveSettingsInput;
    private readonly Button connectButton;
    private readonly Button trustButton;
    private readonly TextBlock statusText;
    private readonly TextBlock fingerprintText;
    private readonly Border certificatePanel;

    public ConnectionView()
    {
        hostInput = new TextBox { PlaceholderText = "Printer IP address or hostname" };
        accessCodeInput = new TextBox { PlaceholderText = "LAN access code", PasswordChar = '\u2022' };
        saveSettingsInput = new CheckBox
        {
            Content = "Save encoded settings beside the application",
            IsChecked = false
        };
        connectButton = new Button
        {
            Content = "Connect",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        trustButton = new Button
        {
            Content = "Trust certificate and connect",
            HorizontalAlignment = HorizontalAlignment.Left
        };
        statusText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.IndianRed,
            IsVisible = false
        };
        fingerprintText = new TextBlock
        {
            FontFamily = FontFamily.Default,
            TextWrapping = TextWrapping.Wrap
        };
        certificatePanel = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(24, 255, 193, 7)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 193, 7)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14),
            IsVisible = false,
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Verify this printer certificate",
                        FontWeight = FontWeight.SemiBold
                    },
                    new TextBlock
                    {
                        Text = "Confirm the SHA-256 fingerprint matches your printer before trusting it.",
                        TextWrapping = TextWrapping.Wrap
                    },
                    fingerprintText,
                    trustButton
                }
            }
        };

        connectButton.Click += (_, _) => ConnectRequested?.Invoke(
            this,
            new ConnectionRequestedEventArgs(
                hostInput.Text?.Trim() ?? string.Empty,
                accessCodeInput.Text ?? string.Empty,
                saveSettingsInput.IsChecked == true));
        trustButton.Click += (_, _) => TrustRequested?.Invoke(this, EventArgs.Empty);

        Content = new ScrollViewer
        {
            Content = new Border
            {
                Width = 460,
                Padding = new Thickness(30),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new StackPanel
                {
                    Spacing = 14,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Bambu X1 Camera",
                            FontSize = 30,
                            FontWeight = FontWeight.SemiBold
                        },
                        new TextBlock
                        {
                            Text = "Connect directly to the printer on your local network.",
                            Foreground = Brushes.Gray,
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(0, 0, 0, 10)
                        },
                        new TextBlock { Text = "Printer address" },
                        hostInput,
                        new TextBlock { Text = "LAN access code" },
                        accessCodeInput,
                        saveSettingsInput,
                        certificatePanel,
                        statusText,
                        connectButton
                    }
                }
            }
        };
    }

    public event EventHandler<ConnectionRequestedEventArgs>? ConnectRequested;

    public event EventHandler? TrustRequested;

    public void LoadSettings(DesktopSettings settings)
    {
        hostInput.Text = settings.Host;
        accessCodeInput.Text = settings.AccessCode;
        saveSettingsInput.IsChecked = true;
    }

    public void ShowCertificate(string sha256)
    {
        fingerprintText.Text = FormatFingerprint(sha256);
        certificatePanel.IsVisible = true;
        statusText.IsVisible = false;
    }

    public void HideCertificate() => certificatePanel.IsVisible = false;

    public void SetBusy(bool busy, string? status = null)
    {
        hostInput.IsEnabled = !busy;
        accessCodeInput.IsEnabled = !busy;
        saveSettingsInput.IsEnabled = !busy;
        connectButton.IsEnabled = !busy;
        trustButton.IsEnabled = !busy;
        if (status is not null)
        {
            statusText.Text = status;
            statusText.Foreground = Brushes.Gray;
            statusText.IsVisible = true;
        }
    }

    public void ShowError(string message)
    {
        statusText.Text = message;
        statusText.Foreground = Brushes.IndianRed;
        statusText.IsVisible = true;
    }

    private static string FormatFingerprint(string value)
    {
        return string.Join(':', Enumerable.Range(0, value.Length / 2).Select(index => value.Substring(index * 2, 2)));
    }
}

internal sealed record ConnectionRequestedEventArgs(string Host, string AccessCode, bool SaveSettings)
{
    public override string ToString() => nameof(ConnectionRequestedEventArgs);
}
