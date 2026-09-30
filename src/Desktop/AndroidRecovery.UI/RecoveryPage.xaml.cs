using AndroidRecovery.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AndroidRecovery.UI;

public sealed partial class RecoveryPage : Page
{
    public RecoveryPage()
    {
        InitializeComponent();
    }

    public event EventHandler? DevicesRequested;

    public event EventHandler? ChangeCategoriesRequested;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is RecoveryViewModel viewModel)
        {
            DataContext = viewModel;
        }

        base.OnNavigatedTo(e);
    }

    private void OnSelectDeviceClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        DevicesRequested?.Invoke(this, EventArgs.Empty);

    private void OnChangeCategoriesClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        ChangeCategoriesRequested?.Invoke(this, EventArgs.Empty);
}