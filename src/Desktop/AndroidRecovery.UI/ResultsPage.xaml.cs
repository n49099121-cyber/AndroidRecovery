using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using AndroidRecovery.ViewModels;
using AndroidRecovery.Models;
using Microsoft.UI.Xaml.Media.Imaging;

namespace AndroidRecovery.UI;

public sealed partial class ResultsPage : Page
{
    private AnalysisViewModel? _viewModel;

    public ResultsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is AnalysisViewModel viewModel)
        {
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            _viewModel = viewModel;
            DataContext = viewModel;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            UpdatePreviewImage(viewModel.PreviewDetails);
        }

        base.OnNavigatedTo(e);
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _viewModel?.SetSelectedItems(ResultList.SelectedItems.OfType<AnalyzedEvidenceItem>());
    }

    private async void OnResultDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (_viewModel is not null && ResultList.SelectedItem is AnalyzedEvidenceItem item)
        {
            await _viewModel.PreviewAsync(item);
        }
    }

    private void OnClosePreviewClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.ClearPreview();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AnalysisViewModel.PreviewDetails) && _viewModel is not null)
        {
            UpdatePreviewImage(_viewModel.PreviewDetails);
        }
    }

    private void UpdatePreviewImage(EvidencePreviewDetails? details)
    {
        if (details?.Kind != EvidencePreviewKind.Image)
        {
            PreviewImage.Source = null;
            return;
        }

        try
        {
            var bitmap = new BitmapImage { DecodePixelWidth = 1600 };
            bitmap.UriSource = new Uri(Path.GetFullPath(details.LocalEvidencePath), UriKind.Absolute);
            PreviewImage.Source = bitmap;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            PreviewImage.Source = null;
        }
    }
}