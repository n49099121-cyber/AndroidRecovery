using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AndroidRecovery.Models;
using AndroidRecovery.Recovery;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AndroidRecovery.ViewModels;

public sealed class RecoveryViewModel : INotifyPropertyChanged
{
    private readonly DeviceViewModel _deviceViewModel;
    private readonly IAcquisitionProvider _acquisitionProvider;
    private readonly IEvidencePackageCatalog _evidenceCatalog;
    private readonly AcquisitionConfiguration _acquisitionConfiguration;
    private readonly IUserDestinationPicker _destinationPicker;
    private readonly IEvidenceFolderOpener _folderOpener;
    private readonly ILogger<RecoveryViewModel> _logger;
    private CancellationTokenSource? _acquisitionCancellation;
    private string _destinationPath;
    private string? _validationMessage;
    private string? _technicalDetails;
    private AcquisitionProgress? _progress;
    private AcquisitionResult? _acquisitionResult;
    private RecoveryWorkflowState _state = RecoveryWorkflowState.Idle;
    private bool _isBusy;

    public RecoveryViewModel(
        DeviceViewModel deviceViewModel,
        IAcquisitionProvider acquisitionProvider,
        IEvidencePackageCatalog evidenceCatalog,
        IOptions<AcquisitionConfiguration> acquisitionConfiguration,
        IUserDestinationPicker destinationPicker,
        IEvidenceFolderOpener folderOpener,
        ILogger<RecoveryViewModel> logger)
    {
        _deviceViewModel = deviceViewModel;
        _acquisitionProvider = acquisitionProvider;
        _evidenceCatalog = evidenceCatalog;
        _acquisitionConfiguration = acquisitionConfiguration.Value;
        _destinationPicker = destinationPicker;
        _folderOpener = folderOpener;
        _logger = logger;
        _destinationPath = string.IsNullOrWhiteSpace(_acquisitionConfiguration.DefaultEvidenceDestination)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AndroidRecovery", "Evidence")
            : _acquisitionConfiguration.DefaultEvidenceDestination;

        Categories =
        [
            new(AcquisitionCategory.Photos, "Photos", "Images in DCIM and Pictures", "Pictures"),
            new(AcquisitionCategory.Videos, "Videos", "Videos in DCIM and Movies", "Video"),
            new(AcquisitionCategory.Audio, "Audio", "Audio in Music and Audio folders", "MusicInfo"),
            new(AcquisitionCategory.Documents, "Documents", "Supported files in Documents and Download", "Page"),
            new(AcquisitionCategory.Downloads, "Downloads", "All files in the shared Download folder", "Download"),
            new(AcquisitionCategory.Dcim, "DCIM", "All files in the shared DCIM folder", "Camera"),
            new(AcquisitionCategory.Pictures, "Pictures", "All files in the shared Pictures folder", "Pictures"),
            new(AcquisitionCategory.Movies, "Movies", "All files in the shared Movies folder", "Video"),
            new(AcquisitionCategory.Music, "Music", "All files in the shared Music folder", "MusicInfo")
        ];
        foreach (var category in Categories)
        {
            category.SelectionChanged += OnCategorySelectionChanged;
        }

        _deviceViewModel.PropertyChanged += OnDeviceViewModelPropertyChanged;
        StartRecoveryCommand = new AsyncCommand(StartRecoveryAsync, () => CanStartAcquisition);
        SelectAllCategoriesCommand = new AsyncCommand(SelectAllCategoriesAsync, () => !IsBusy);
        ClearAllCategoriesCommand = new AsyncCommand(ClearAllCategoriesAsync, () => !IsBusy);
        ContinueToAcquisitionCommand = new AsyncCommand(ContinueToAcquisitionAsync, () => CanContinue);
        CancelAcquisitionCommand = new AsyncCommand(CancelAcquisitionAsync, () => IsAcquiring);
        BrowseDestinationCommand = new AsyncCommand(BrowseDestinationAsync, () => !IsBusy);
        OpenEvidenceCommand = new AsyncCommand(OpenEvidenceAsync, () => EvidencePackagePath is not null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? ContinueRequested;

    public ObservableCollection<RecoveryCategoryOption> Categories { get; }

    public AsyncCommand StartRecoveryCommand { get; }

    public AsyncCommand SelectAllCategoriesCommand { get; }

    public AsyncCommand ClearAllCategoriesCommand { get; }

    public AsyncCommand ContinueToAcquisitionCommand { get; }

    public AsyncCommand CancelAcquisitionCommand { get; }

    public AsyncCommand BrowseDestinationCommand { get; }

    public AsyncCommand OpenEvidenceCommand { get; }

    public AndroidDevice? SelectedDevice => _deviceViewModel.SelectedDevice;

    public string SelectedDeviceName => _deviceViewModel.SelectedDeviceName;

    public string SelectedDeviceIdentity => _deviceViewModel.SelectedDeviceIdentity;

    public string SelectedAdbState => _deviceViewModel.SelectedAdbState;

    public bool AdbAvailable => _deviceViewModel.AdbAvailable;

    public string DestinationPath
    {
        get => _destinationPath;
        set
        {
            if (SetProperty(ref _destinationPath, value))
            {
                _validationMessage = null;
                OnPropertyChanged(nameof(CanStartAcquisition));
                OnPropertyChanged(nameof(CanContinue));
                OnPropertyChanged(nameof(ValidationMessage));
                OnPropertyChanged(nameof(HasValidationMessage));
                StartRecoveryCommand.NotifyCanExecuteChanged();
                SelectAllCategoriesCommand.NotifyCanExecuteChanged();
                ClearAllCategoriesCommand.NotifyCanExecuteChanged();
                ContinueToAcquisitionCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsAcquiring));
                OnPropertyChanged(nameof(IsEditingWorkflow));
                OnPropertyChanged(nameof(CanStartAcquisition));
                foreach (var category in Categories)
                {
                    category.IsEnabled = !value;
                }
                StartRecoveryCommand.NotifyCanExecuteChanged();
                CancelAcquisitionCommand.NotifyCanExecuteChanged();
                BrowseDestinationCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsAcquiring => IsBusy && State is RecoveryWorkflowState.Validating or RecoveryWorkflowState.Preparing
        or RecoveryWorkflowState.Acquiring or RecoveryWorkflowState.Cancelling;

    public bool CanStartAcquisition => !IsBusy && SelectedDevice?.Authorized == true && AdbAvailable
        && Categories.Any(category => category.IsSelected)
        && !string.IsNullOrWhiteSpace(DestinationPath);

    public bool HasSelectedCategories => Categories.Any(category => category.IsSelected);

    public bool CanContinue => !IsBusy && HasSelectedCategories;

    public bool HasAuthorizedDevice => SelectedDevice?.Authorized == true && AdbAvailable;

    public string DeviceReadinessMessage => SelectedDevice is null
        ? "No device selected. Connect your phone by USB, unlock it, and accept its authorization prompt."
        : SelectedDevice.AdbState switch
        {
            AdbDeviceState.Unauthorized => "Unlock your phone and accept the USB debugging authorization prompt.",
            AdbDeviceState.Offline => "Device offline. Reconnect the phone and refresh Devices.",
            AdbDeviceState.Device when !AdbAvailable => "ADB is unavailable on this PC. Check the configured app-local Platform Tools.",
            AdbDeviceState.Device => "Authorized device connected and ready.",
            _ => "Device state is unknown. Acquisition is unavailable."
        };

    public string SelectedCategoriesSummary => HasSelectedCategories
        ? string.Join(", ", Categories.Where(category => category.IsSelected).Select(category => category.Title))
        : "No data types selected";

    public string AcquisitionMethod => "ADB";

    public string AdbStatus => AdbAvailable ? "ADB executable available" : "ADB executable unavailable";

    public string StateTitle => State switch
    {
        RecoveryWorkflowState.Validating => "Validating acquisition",
        RecoveryWorkflowState.Preparing => "Preparing acquisition",
        RecoveryWorkflowState.Acquiring => "Acquiring device data",
        RecoveryWorkflowState.Cancelling => "Cancelling acquisition",
        RecoveryWorkflowState.Completed => "Acquisition complete",
        RecoveryWorkflowState.CompletedWithWarnings => "Acquisition completed with warnings",
        RecoveryWorkflowState.Failed => "Acquisition failed",
        RecoveryWorkflowState.Cancelled => "Acquisition cancelled",
        _ => "Recover Android Data"
    };

    public bool IsIdle => State is RecoveryWorkflowState.Idle or RecoveryWorkflowState.Failed or RecoveryWorkflowState.Cancelled;

    public bool IsEditingWorkflow => State is RecoveryWorkflowState.Idle or RecoveryWorkflowState.Failed
        or RecoveryWorkflowState.Cancelled;

    public bool IsFailedOrCancelled => State is RecoveryWorkflowState.Failed or RecoveryWorkflowState.Cancelled;

    public bool IsPreparing => State is RecoveryWorkflowState.Validating or RecoveryWorkflowState.Preparing;

    public bool IsAcquisitionRunning => State is RecoveryWorkflowState.Acquiring or RecoveryWorkflowState.Cancelling;

    public bool IsCompleted => State is RecoveryWorkflowState.Completed or RecoveryWorkflowState.CompletedWithWarnings;

    public bool IsFailed => State == RecoveryWorkflowState.Failed;

    public bool IsCancelled => State == RecoveryWorkflowState.Cancelled;

    public RecoveryWorkflowState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(StateTitle));
                OnPropertyChanged(nameof(IsIdle));
                OnPropertyChanged(nameof(IsPreparing));
                OnPropertyChanged(nameof(IsAcquisitionRunning));
                OnPropertyChanged(nameof(IsCompleted));
                OnPropertyChanged(nameof(IsFailed));
                OnPropertyChanged(nameof(IsCancelled));
                OnPropertyChanged(nameof(HasPreparingProgress));
                OnPropertyChanged(nameof(IsAcquiring));
                OnPropertyChanged(nameof(IsEditingWorkflow));
                OnPropertyChanged(nameof(IsFailedOrCancelled));
            }
        }
    }

    public string? ValidationMessage
    {
        get => _validationMessage ?? GetValidationMessage();
        private set => SetProperty(ref _validationMessage, value);
    }

    public bool HasValidationMessage => !string.IsNullOrWhiteSpace(ValidationMessage);

    public string? TechnicalDetails
    {
        get => _technicalDetails;
        private set => SetProperty(ref _technicalDetails, value);
    }

    public AcquisitionProgress? Progress
    {
        get => _progress;
        private set
        {
            if (SetProperty(ref _progress, value))
            {
                OnPropertyChanged(nameof(ProgressPercent));
                OnPropertyChanged(nameof(ProgressLabel));
                OnPropertyChanged(nameof(FilesSummary));
                OnPropertyChanged(nameof(BytesSummary));
                OnPropertyChanged(nameof(ElapsedLabel));
                OnPropertyChanged(nameof(CurrentSourceLabel));
                OnPropertyChanged(nameof(CurrentDestinationLabel));
                OnPropertyChanged(nameof(EstimatedRemainingLabel));
                OnPropertyChanged(nameof(HasEstimatedRemaining));
                OnPropertyChanged(nameof(ErrorCount));
                OnPropertyChanged(nameof(WarningCount));
                OnPropertyChanged(nameof(HasPreparingProgress));
                OnPropertyChanged(nameof(HasAuthorizedDevice));
                OnPropertyChanged(nameof(SelectedCategoriesSummary));
                OnPropertyChanged(nameof(DiscoverySummary));
                OnPropertyChanged(nameof(DiscoveryBytesLabel));
                OnPropertyChanged(nameof(HasValidationMessage));
            }
        }
    }

    public bool HasPreparingProgress => IsPreparing && Progress?.Status == AcquisitionStatus.Preparing;

    public string DiscoverySummary => Progress is null
        ? "Preparing evidence package"
        : $"{Progress.FilesDiscovered:N0} files discovered";

    public string DiscoveryBytesLabel => Progress is null
        ? "File sizes will be calculated during enumeration"
        : $"{FormatBytes(Progress.BytesDiscovered)} with known sizes";

    public double ProgressPercent => Progress?.PercentComplete ?? 0;

    public string ProgressLabel => Progress is null ? "Preparing" : $"{Progress.PercentComplete:0}%";

    public string FilesSummary => Progress is null
        ? "Discovering files"
        : $"{Progress.FilesAcquired:N0} / {Progress.FilesDiscovered:N0} files";

    public string BytesSummary => Progress is null
        ? "Calculating size"
        : Progress.IsByteTotalReliable
            ? $"{FormatBytes(Progress.BytesAcquired)} / {FormatBytes(Progress.BytesDiscovered)}"
            : $"{FormatBytes(Progress.BytesAcquired)} acquired; some file sizes are unavailable";

    public string ElapsedLabel => Progress?.ElapsedTime.ToString(@"hh\:mm\:ss") ?? "00:00:00";

    public string CurrentSourceLabel => Progress?.CurrentSourcePath ?? "Waiting for file list";

    public string CurrentDestinationLabel => Progress?.CurrentDestinationPath ?? "";

    public bool HasEstimatedRemaining => Progress?.EstimatedRemainingTime is not null;

    public string EstimatedRemainingLabel => Progress?.EstimatedRemainingTime?.ToString(@"hh\:mm\:ss") ?? "";

    public int ErrorCount => Progress?.ErrorCount ?? AcquisitionResult?.Errors.Count ?? 0;

    public int WarningCount => Progress?.WarningCount ?? AcquisitionResult?.Warnings.Count ?? 0;

    public AcquisitionResult? AcquisitionResult
    {
        get => _acquisitionResult;
        private set
        {
            if (SetProperty(ref _acquisitionResult, value))
            {
                OnPropertyChanged(nameof(EvidencePackagePath));
                OnPropertyChanged(nameof(HasEvidencePackage));
                OnPropertyChanged(nameof(VerificationLabel));
                OnPropertyChanged(nameof(SummaryFilesAcquired));
                OnPropertyChanged(nameof(SummaryBytesAcquired));
                OnPropertyChanged(nameof(DurationLabel));
                OnPropertyChanged(nameof(ErrorCount));
                OnPropertyChanged(nameof(WarningCount));
                OpenEvidenceCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? EvidencePackagePath => AcquisitionResult?.EvidencePackage?.RootPath;

    public bool HasEvidencePackage => EvidencePackagePath is not null;

    public string VerificationLabel => AcquisitionResult?.Verification switch
    {
        { Succeeded: true } => "Evidence verified",
        { Succeeded: false } => "Verification failed or incomplete",
        _ => "Verification not run"
    };

    public string SummaryFilesAcquired => AcquisitionResult?.Session.TotalFilesAcquired.ToString("N0") ?? "0";

    public string SummaryBytesAcquired => FormatBytes(AcquisitionResult?.Session.TotalBytesAcquired ?? 0);

    public string DurationLabel => AcquisitionResult?.Duration.ToString(@"hh\:mm\:ss") ?? "00:00:00";

    public string SafetyMessage => "Acquisition uses authorized ADB read operations only. It does not unlock the phone, change settings, or delete or modify device files. It cannot recover deleted files.";

    private async Task StartRecoveryAsync()
    {
        if (!CanStartAcquisition || SelectedDevice is null)
        {
            ValidationMessage = GetValidationMessage();
            return;
        }

        State = RecoveryWorkflowState.Validating;
        ValidationMessage = null;
        TechnicalDetails = null;
        AcquisitionResult = null;
        Progress = null;
        IsBusy = true;
        _acquisitionCancellation = new CancellationTokenSource();

        try
        {
            await ValidateDestinationAsync(_acquisitionCancellation.Token);
            State = RecoveryWorkflowState.Preparing;
            var options = new AcquisitionOptions
            {
                SelectedCategories = Categories.Where(category => category.IsSelected)
                    .Select(category => category.Category).ToHashSet(),
                AcquisitionMethod = AcquisitionMethod,
                DestinationPath = Path.GetFullPath(DestinationPath),
                CalculateHashes = _acquisitionConfiguration.CalculateSha256,
                VerifyAfterAcquisition = _acquisitionConfiguration.VerifyAfterAcquisition,
                PreserveFolderStructure = _acquisitionConfiguration.PreserveFolderStructure,
                GenerateReport = _acquisitionConfiguration.GenerateReport
            };

            var contextProgress = new Progress<AcquisitionProgress>(value =>
            {
                Progress = value;
                if (State == RecoveryWorkflowState.Cancelling)
                {
                    return;
                }

                if (value.Status == AcquisitionStatus.Preparing)
                {
                    State = RecoveryWorkflowState.Preparing;
                }
                else if (value.Status == AcquisitionStatus.Running)
                {
                    State = RecoveryWorkflowState.Acquiring;
                }
            });
            var result = await _acquisitionProvider.AcquireAsync(SelectedDevice, options, contextProgress,
                _acquisitionCancellation.Token);
            AcquisitionResult = result;
            if (result.EvidencePackage is not null)
            {
                try
                {
                    await _evidenceCatalog.RegisterAsync(new EvidencePackageSummary
                    {
                        SessionId = result.Session.SessionId,
                        PackagePath = result.EvidencePackage.RootPath,
                        DeviceManufacturer = result.Session.DeviceManufacturer,
                        DeviceModel = result.Session.DeviceModel,
                        AcquiredUtc = result.Session.EndTimeUtc ?? DateTimeOffset.UtcNow,
                        AcquisitionMethod = result.Session.AcquisitionMethod,
                        FileCount = result.Session.TotalFilesAcquired,
                        BytesAcquired = result.Session.TotalBytesAcquired,
                        Status = result.Status,
                        IsVerified = result.Verification?.Succeeded
                    }, CancellationToken.None);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
                {
                    _logger.LogWarning("Evidence package could not be added to the local catalog ({ExceptionType})",
                        exception.GetType().Name);
                }
            }

            State = result.Status switch
            {
                AcquisitionStatus.Completed => RecoveryWorkflowState.Completed,
                AcquisitionStatus.CompletedWithWarnings => RecoveryWorkflowState.CompletedWithWarnings,
                AcquisitionStatus.Cancelled => RecoveryWorkflowState.Cancelled,
                _ => RecoveryWorkflowState.Failed
            };
            ValidationMessage = result.Status switch
            {
                AcquisitionStatus.Completed => "Acquisition completed and the evidence package was verified.",
                AcquisitionStatus.CompletedWithWarnings => "The package was created, but some paths/files or verification checks had warnings.",
                AcquisitionStatus.Cancelled => "Acquisition cancelled. Files already acquired were preserved.",
                _ => "Acquisition failed. Any evidence already copied was preserved."
            };
            TechnicalDetails = string.Join(Environment.NewLine,
                result.Errors.Concat(result.Warnings).Select(error => $"{error.Severity}: {error.ErrorCode}: {error.Message}"));
        }
        catch (OperationCanceledException) when (_acquisitionCancellation.IsCancellationRequested)
        {
            State = RecoveryWorkflowState.Cancelled;
            ValidationMessage = "Acquisition cancelled. Any evidence already copied was preserved.";
        }
        catch (Exception exception)
        {
            _logger.LogError("Recovery workflow failed ({ExceptionType})", exception.GetType().Name);
            State = RecoveryWorkflowState.Failed;
            ValidationMessage = "The acquisition could not be started or finalized.";
            TechnicalDetails = exception.GetType().Name;
        }
        finally
        {
            _acquisitionCancellation?.Dispose();
            _acquisitionCancellation = null;
            IsBusy = false;
            OnPropertyChanged(nameof(ErrorCount));
            OnPropertyChanged(nameof(WarningCount));
        }
    }

    private Task CancelAcquisitionAsync()
    {
        if (_acquisitionCancellation is not null && ! _acquisitionCancellation.IsCancellationRequested)
        {
            State = RecoveryWorkflowState.Cancelling;
            _acquisitionCancellation.Cancel();
        }

        return Task.CompletedTask;
    }

    private async Task BrowseDestinationAsync()
    {
        var path = await _destinationPicker.PickFolderAsync(DestinationPath, CancellationToken.None);
        if (!string.IsNullOrWhiteSpace(path))
        {
            DestinationPath = path;
        }
    }

    private async Task OpenEvidenceAsync()
    {
        if (EvidencePackagePath is not null)
        {
            await _folderOpener.OpenFolderAsync(EvidencePackagePath, CancellationToken.None);
        }
    }

    private async Task ValidateDestinationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(DestinationPath);
        Directory.CreateDirectory(fullPath);
        var probePath = Path.Combine(fullPath, $".androidrecovery-write-test-{Guid.NewGuid():N}");
        try
        {
            await using var probe = new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                1, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            await probe.WriteAsync(new byte[] { 0 }, cancellationToken);
            await probe.FlushAsync(cancellationToken);
        }
        finally
        {
            if (File.Exists(probePath))
            {
                File.Delete(probePath);
            }
        }

        DestinationPath = fullPath;
    }

    private string GetValidationMessage()
    {
        if (SelectedDevice is null)
        {
            return "Select a device in Devices before starting acquisition.";
        }

        if (SelectedDevice.AdbState == AdbDeviceState.Unauthorized)
        {
            return "Unlock your Android device and accept the USB debugging authorization prompt.";
        }

        if (SelectedDevice.AdbState == AdbDeviceState.Offline)
        {
            return "The selected device is offline. Check USB and refresh Devices.";
        }

        if (!SelectedDevice.Authorized)
        {
            return "The device state is unknown; acquisition is disabled.";
        }

        if (!AdbAvailable)
        {
            return "ADB Platform Tools are unavailable. Add trusted adb.exe at the configured app-local path.";
        }

        if (!HasSelectedCategories)
        {
            return "Select at least one data category to acquire.";
        }

        if (string.IsNullOrWhiteSpace(DestinationPath))
        {
            return "Choose an evidence destination directory.";
        }

        return "Ready to acquire accessible files using read-only ADB operations.";
    }

    private void OnCategorySelectionChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(HasSelectedCategories));
            OnPropertyChanged(nameof(CanContinue));
            OnPropertyChanged(nameof(SelectedCategoriesSummary));
            OnPropertyChanged(nameof(DeviceReadinessMessage));
        OnPropertyChanged(nameof(CanStartAcquisition));
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(HasValidationMessage));
        StartRecoveryCommand.NotifyCanExecuteChanged();
    }

    private void OnDeviceViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DeviceViewModel.SelectedDevice)
            or nameof(DeviceViewModel.SelectedDeviceName)
            or nameof(DeviceViewModel.SelectedDeviceIdentity)
            or nameof(DeviceViewModel.SelectedAdbState)
            or nameof(DeviceViewModel.AdbAvailable))
        {
            OnPropertyChanged(nameof(SelectedDevice));
            OnPropertyChanged(nameof(SelectedDeviceName));
            OnPropertyChanged(nameof(SelectedDeviceIdentity));
            OnPropertyChanged(nameof(SelectedAdbState));
            OnPropertyChanged(nameof(AdbAvailable));
            OnPropertyChanged(nameof(AdbStatus));
            OnPropertyChanged(nameof(CanStartAcquisition));
            OnPropertyChanged(nameof(CanContinue));
            OnPropertyChanged(nameof(HasAuthorizedDevice));
            OnPropertyChanged(nameof(DeviceReadinessMessage));
            OnPropertyChanged(nameof(ValidationMessage));
            OnPropertyChanged(nameof(HasValidationMessage));
            StartRecoveryCommand.NotifyCanExecuteChanged();
            ContinueToAcquisitionCommand.NotifyCanExecuteChanged();
        }
    }

    private Task SelectAllCategoriesAsync()
    {
        foreach (var category in Categories)
        {
            category.IsSelected = true;
        }

        return Task.CompletedTask;
    }

    private Task ClearAllCategoriesAsync()
    {
        foreach (var category in Categories)
        {
            category.IsSelected = false;
        }

        return Task.CompletedTask;
    }

    private Task ContinueToAcquisitionAsync()
    {
        if (CanContinue)
        {
            ContinueRequested?.Invoke(this, EventArgs.Empty);
        }

        return Task.CompletedTask;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        var value = (double)bytes;
        var units = new[] { "KB", "MB", "GB", "TB" };
        foreach (var unit in units)
        {
            value /= 1024;
            if (value < 1024 || unit == "TB")
            {
                return $"{value:0.##} {unit}";
            }
        }

        return $"{value:0.##} TB";
    }
}