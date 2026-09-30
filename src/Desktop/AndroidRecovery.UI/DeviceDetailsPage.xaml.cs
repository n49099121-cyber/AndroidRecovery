using AndroidRecovery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AndroidRecovery.UI;

public sealed partial class DeviceDetailsPage : Page
{
    public DeviceDetailsPage()
    {
        InitializeComponent();
    }

    public event EventHandler? DevicesRequested;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is DeviceViewModel viewModel)
        {
            DataContext = viewModel;
        }

        base.OnNavigatedTo(e);
    }

    private void OnBackToDevicesClick(object sender, RoutedEventArgs e) =>
        DevicesRequested?.Invoke(this, EventArgs.Empty);
}