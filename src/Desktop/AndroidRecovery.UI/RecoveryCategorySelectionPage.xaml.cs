using AndroidRecovery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AndroidRecovery.UI;

public sealed partial class RecoveryCategorySelectionPage : Page
{
    public RecoveryCategorySelectionPage()
    {
        InitializeComponent();
    }

    public event EventHandler? ConnectDeviceRequested;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is RecoveryViewModel viewModel)
        {
            DataContext = viewModel;
        }

        base.OnNavigatedTo(e);
    }

    private void OnConnectDeviceClick(object sender, RoutedEventArgs e) =>
        ConnectDeviceRequested?.Invoke(this, EventArgs.Empty);
}