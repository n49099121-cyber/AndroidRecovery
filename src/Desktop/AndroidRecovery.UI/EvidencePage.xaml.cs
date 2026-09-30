using AndroidRecovery.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AndroidRecovery.UI;

public sealed partial class EvidencePage : Page
{
    public EvidencePage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is EvidenceViewModel viewModel)
        {
            DataContext = viewModel;
            viewModel.RefreshCommand.Execute(null);
        }

        base.OnNavigatedTo(e);
    }
}