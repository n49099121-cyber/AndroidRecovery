using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AndroidRecovery.UI;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    public event EventHandler? DiagnosticsRequested;

    private void OnOpenDiagnosticsClick(object sender, RoutedEventArgs e) =>
        DiagnosticsRequested?.Invoke(this, EventArgs.Empty);
}