using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using AndroidRecovery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AndroidRecovery.UI;

public sealed partial class DiagnosticsPage : Page
{
    private readonly string _logDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AndroidRecovery", "logs");

    public DiagnosticsPage()
    {
        InitializeComponent();
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "Unknown";
        VersionValue.Text = version;
        ArchitectureValue.Text = RuntimeInformation.ProcessArchitecture.ToString();
        WindowsValue.Text = Environment.OSVersion.Version.ToString();
        LogPathValue.Text = _logDirectory;
    }

    public event EventHandler? SettingsRequested;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is DeviceViewModel viewModel)
        {
            DataContext = viewModel;
        }

        base.OnNavigatedTo(e);
    }

    private void OnBackToSettingsClick(object sender, RoutedEventArgs e) =>
        SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void OnOpenLogsClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_logDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", _logDirectory) { UseShellExecute = true });
    }
}