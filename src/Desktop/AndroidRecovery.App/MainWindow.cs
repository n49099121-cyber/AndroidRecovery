using Microsoft.UI;
using Microsoft.UI.Windowing;
using AndroidRecovery.UI;
using AndroidRecovery.ViewModels;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace AndroidRecovery.App;

public sealed class MainWindow : Window
{
    public MainWindow(DeviceViewModel viewModel, RecoveryViewModel recoveryViewModel,
        EvidenceViewModel evidenceViewModel, AnalysisViewModel analysisViewModel,
        WindowHandleReference windowHandleReference)
    {
        Title = "Android Data Recovery Tool";
        Content = new AppShell(viewModel, recoveryViewModel, evidenceViewModel, analysisViewModel);
        Activated += OnWindowActivated;
        WindowHandleReference = windowHandleReference;
    }

    private WindowHandleReference WindowHandleReference { get; }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnWindowActivated;
        var windowHandle = WindowNative.GetWindowHandle(this);
        WindowHandleReference.Set(windowHandle);
        var windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 1100;
            presenter.PreferredMinimumHeight = 700;
        }

        appWindow.Resize(new SizeInt32(1280, 800));
    }
}