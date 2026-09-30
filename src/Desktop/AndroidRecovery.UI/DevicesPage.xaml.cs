using AndroidRecovery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AndroidRecovery.UI;

public sealed partial class DevicesPage : Page
{
    public DevicesPage()
    {
        InitializeComponent();
    }

    public event EventHandler? DeviceDetailsRequested;

    public event EventHandler? ContinueToAcquisitionRequested;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is DeviceViewModel viewModel)
        {
            DataContext = viewModel;
        }

        base.OnNavigatedTo(e);
    }

    private void OnViewDetailsClick(object sender, RoutedEventArgs e) =>
        DeviceDetailsRequested?.Invoke(this, EventArgs.Empty);

    private void OnContinueClick(object sender, RoutedEventArgs e) =>
        ContinueToAcquisitionRequested?.Invoke(this, EventArgs.Empty);
}