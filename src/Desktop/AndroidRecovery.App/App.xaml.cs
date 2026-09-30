using AndroidRecovery.Adb;
using AndroidRecovery.Core;
using AndroidRecovery.DeviceDetection;
using AndroidRecovery.FileSystem;
using AndroidRecovery.Logging;
using AndroidRecovery.Recovery;
using AndroidRecovery.UI;
using AndroidRecovery.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace AndroidRecovery.App;

public partial class App : Application
{
    private IHost? _host;
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _host = Host.CreateDefaultBuilder()
                .UseAndroidRecoveryLogging()
                .ConfigureServices((context, services) =>
                {
                    services.Configure<AdbOptions>(context.Configuration.GetSection("Adb"));
                    services.Configure<AcquisitionConfiguration>(context.Configuration.GetSection("Acquisition"));
                    services.AddSingleton<IAdbService, AdbService>();
                    services.AddSingleton<IDeviceDetectionService, DeviceDetectionService>();
                    services.AddSingleton<IAnalysisService, EvidenceAnalysisService>();
                    services.AddSingleton<IPreviewService, EvidencePreviewService>();
                    services.AddSingleton<IRecoveryService, FileRecoveryService>();
                    services.AddSingleton<IPreviewService, EvidencePreviewService>();
                    services.AddSingleton<IRecoveryService, FileRecoveryService>();
                    services.AddSingleton<IDeviceFileEnumerator, AdbDeviceFileEnumerator>();
                    services.AddSingleton<IHashService, Sha256HashService>();
                    services.AddSingleton<IEvidencePackageWriterFactory, EvidencePackageWriterFactory>();
                    services.AddSingleton<IEvidenceVerifier, EvidenceVerifier>();
                    services.AddSingleton<IEvidencePackageCatalog, JsonEvidencePackageCatalog>();
                    services.AddSingleton<IAcquisitionProvider, AdbAcquisitionProvider>();
                    services.AddSingleton<WindowHandleReference>();
                    services.AddSingleton<IUserDestinationPicker, WindowsUserDestinationPicker>();
                    services.AddSingleton<IEvidenceFolderOpener, WindowsEvidenceFolderOpener>();
                    services.AddSingleton<DeviceViewModel>();
                    services.AddSingleton<RecoveryViewModel>();
                    services.AddSingleton<EvidenceViewModel>();
                    services.AddSingleton<AnalysisViewModel>();
                })
                .Build();

            await _host.StartAsync();
            var viewModel = _host.Services.GetRequiredService<DeviceViewModel>();
            var recoveryViewModel = _host.Services.GetRequiredService<RecoveryViewModel>();
            var evidenceViewModel = _host.Services.GetRequiredService<EvidenceViewModel>();
            var analysisViewModel = _host.Services.GetRequiredService<AnalysisViewModel>();
            var windowHandleReference = _host.Services.GetRequiredService<WindowHandleReference>();
            _window = new MainWindow(viewModel, recoveryViewModel, evidenceViewModel, analysisViewModel,
                windowHandleReference);
            _window.Closed += (_, _) => _host.Dispose();
            _window.Activate();
        }
        catch (Exception exception)
        {
            var logger = _host?.Services.GetService<ILogger<App>>();
            logger?.LogCritical(exception, "Application startup failed");
            _window ??= new Window { Title = "Android Data Recovery Tool" };
            _window.Content = new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = "The application could not start. Check the log files under Local AppData\\AndroidRecovery\\logs.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(24)
            };
            _window.Activate();
        }
    }
}