using AndroidRecovery.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AndroidRecovery.UI;

public sealed partial class HomePage : Page
{
    public HomePage()
    {
        InitializeComponent();
    }

    public event EventHandler? DevicesRequested;

    public event EventHandler? StartRecoveryRequested;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is DeviceViewModel viewModel)
        {
            DataContext = viewModel;
        }

        base.OnNavigatedTo(e);
    }

    private void OnViewDevicesClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        DevicesRequested?.Invoke(this, EventArgs.Empty);

    private void OnStartRecoveryClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        StartRecoveryRequested?.Invoke(this, EventArgs.Empty);
}