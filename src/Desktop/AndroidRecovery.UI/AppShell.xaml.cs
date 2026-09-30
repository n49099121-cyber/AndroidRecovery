using AndroidRecovery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AndroidRecovery.UI;

public sealed partial class AppShell : UserControl
{
    private readonly DispatcherTimer _deviceRefreshTimer;

    public AppShell(DeviceViewModel viewModel, RecoveryViewModel recoveryViewModel,
        EvidenceViewModel evidenceViewModel, AnalysisViewModel analysisViewModel)
    {
        ViewModel = viewModel;
        RecoveryViewModel = recoveryViewModel;
        EvidenceViewModel = evidenceViewModel;
        AnalysisViewModel = analysisViewModel;
        InitializeComponent();
        PageFrame.Navigated += OnPageNavigated;
        _deviceRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _deviceRefreshTimer.Tick += (_, _) =>
        {
            if (ViewModel.AdbAvailable && !ViewModel.IsBusy)
            {
                ViewModel.RefreshCommand.Execute(null);
            }
        };
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public DeviceViewModel ViewModel { get; }

    public RecoveryViewModel RecoveryViewModel { get; }

    public EvidenceViewModel EvidenceViewModel { get; }

    public AnalysisViewModel AnalysisViewModel { get; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        EvidenceViewModel.AnalysisRequested -= OnAnalysisRequested;
        EvidenceViewModel.AnalysisRequested += OnAnalysisRequested;
        RecoveryViewModel.ContinueRequested -= OnContinueToAcquisitionRequested;
        RecoveryViewModel.ContinueRequested += OnContinueToAcquisitionRequested;
        ShellNavigation.SelectedItem = HomeNavigationItem;
        NavigateTo("Home");
        ViewModel.RefreshCommand.Execute(null);
        _deviceRefreshTimer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _deviceRefreshTimer.Stop();
        EvidenceViewModel.AnalysisRequested -= OnAnalysisRequested;
        RecoveryViewModel.ContinueRequested -= OnContinueToAcquisitionRequested;
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is string route)
        {
            NavigateTo(route);
        }
    }

    private void OnPageNavigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        if (e.Content is HomePage homePage)
        {
            homePage.DevicesRequested -= OnDevicesRequested;
            homePage.DevicesRequested += OnDevicesRequested;
            homePage.StartRecoveryRequested -= OnStartRecoveryRequested;
            homePage.StartRecoveryRequested += OnStartRecoveryRequested;
        }
        else if (e.Content is DevicesPage devicesPage)
        {
            devicesPage.DeviceDetailsRequested -= OnDeviceDetailsRequested;
            devicesPage.DeviceDetailsRequested += OnDeviceDetailsRequested;
            devicesPage.ContinueToAcquisitionRequested -= OnContinueToAcquisitionRequested;
            devicesPage.ContinueToAcquisitionRequested += OnContinueToAcquisitionRequested;
        }
        else if (e.Content is RecoveryCategorySelectionPage categoryPage)
        {
            categoryPage.ConnectDeviceRequested -= OnDevicesRequested;
            categoryPage.ConnectDeviceRequested += OnDevicesRequested;
        }
        else if (e.Content is RecoveryPage recoveryPage)
        {
            recoveryPage.DevicesRequested -= OnDevicesRequested;
            recoveryPage.DevicesRequested += OnDevicesRequested;
            recoveryPage.ChangeCategoriesRequested -= OnStartRecoveryRequested;
            recoveryPage.ChangeCategoriesRequested += OnStartRecoveryRequested;
        }
        else if (e.Content is DeviceDetailsPage detailsPage)
        {
            detailsPage.DevicesRequested -= OnDevicesRequested;
            detailsPage.DevicesRequested += OnDevicesRequested;
        }
        else if (e.Content is SettingsPage settingsPage)
        {
            settingsPage.DiagnosticsRequested -= OnDiagnosticsRequested;
            settingsPage.DiagnosticsRequested += OnDiagnosticsRequested;
        }
        else if (e.Content is DiagnosticsPage diagnosticsPage)
        {
            diagnosticsPage.SettingsRequested -= OnSettingsRequested;
            diagnosticsPage.SettingsRequested += OnSettingsRequested;
        }
    }

    private void OnDevicesRequested(object? sender, EventArgs e) => ShowDevices();

    private void OnStartRecoveryRequested(object? sender, EventArgs e) => NavigateTo("Recovery");

    private void OnContinueToAcquisitionRequested(object? sender, EventArgs e)
    {
        if (RecoveryViewModel.HasAuthorizedDevice)
        {
            NavigateTo("Acquisition");
            return;
        }

        ShowDevices();
    }

    private void OnDeviceDetailsRequested(object? sender, EventArgs e) => ShowDeviceDetails();

    private void OnDiagnosticsRequested(object? sender, EventArgs e) => NavigateTo("Diagnostics");

    private void OnSettingsRequested(object? sender, EventArgs e) => NavigateTo("Settings");

    private void NavigateTo(string route)
    {
        switch (route)
        {
            case "Home":
                PageFrame.Navigate(typeof(HomePage), ViewModel);
                break;
            case "Devices":
                PageFrame.Navigate(typeof(DevicesPage), ViewModel);
                break;
            case "Recovery":
                PageFrame.Navigate(typeof(RecoveryCategorySelectionPage), RecoveryViewModel);
                break;
            case "Acquisition":
                PageFrame.Navigate(typeof(RecoveryPage), RecoveryViewModel);
                break;
            case "Evidence":
                PageFrame.Navigate(typeof(EvidencePage), EvidenceViewModel);
                break;
            case "Results":
                PageFrame.Navigate(typeof(ResultsPage), AnalysisViewModel);
                break;
            case "DeviceDetails":
                PageFrame.Navigate(typeof(DeviceDetailsPage), ViewModel);
                break;
            case "Settings":
                PageFrame.Navigate(typeof(SettingsPage));
                break;
            case "Diagnostics":
                PageFrame.Navigate(typeof(DiagnosticsPage), ViewModel);
                break;
            case "About":
                PageFrame.Navigate(typeof(AboutPage));
                break;
            default:
                PageFrame.Navigate(typeof(ComingSoonPage), ComingSoonRoute.For(route));
                break;
        }
    }

    private void OnAnalysisRequested(object? sender, EventArgs e)
    {
        if (EvidenceViewModel.SelectedPackage is not { } package)
        {
            return;
        }

        AnalysisViewModel.SetPackagePath(package.PackagePath);
        NavigateTo("Results");
        AnalysisViewModel.AnalyzeCommand.Execute(null);
    }

    public void ShowDevices()
    {
        var devicesItem = ShellNavigation.MenuItems
            .OfType<NavigationViewItem>()
            .First(item => Equals(item.Tag, "Devices"));
        if (ReferenceEquals(ShellNavigation.SelectedItem, devicesItem))
        {
            NavigateTo("Devices");
        }
        else
        {
            ShellNavigation.SelectedItem = devicesItem;
        }
    }

    public void ShowDeviceDetails()
    {
        NavigateTo("DeviceDetails");
    }
}

public sealed record ComingSoonRoute(string Title, string Description)
{
    public static ComingSoonRoute For(string route) => route switch
    {
        "Recovery" => new("Recovery", "Recovery selection and acquisition are not implemented yet. Device discovery is available under Devices."),
        "Evidence" => new("Evidence", "Browse and verify evidence packages created by authorized acquisition."),
        "Results" => new("Results", "Analysis results and file preview are not implemented yet."),
        "Settings" => new("Settings", "Application settings will be available in a later milestone."),
        "About" => new("About AndroidRecovery", "AndroidRecovery is an authorized, read-only-first Android device analysis application. Data acquisition and recovery workflows are not available in this build."),
        _ => new("Not available", "This destination is not available in this build.")
    };
}