using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AndroidRecovery.DeviceDetection;
using AndroidRecovery.Models;
using Microsoft.Extensions.Logging;

namespace AndroidRecovery.ViewModels;

public sealed class DeviceViewModel : INotifyPropertyChanged
{
    private readonly IDeviceDetectionService _deviceDetectionService;
    private readonly ILogger<DeviceViewModel> _logger;
    private CancellationTokenSource? _refreshCancellation;
    private AndroidDevice? _selectedDevice;
    private bool _isBusy;
    private bool _adbAvailable;
    private string _statusMessage = "Checking for Android devices...";
    private string? _technicalDetails;
    private string _adbExecutablePath = "Not resolved";

    public DeviceViewModel(IDeviceDetectionService deviceDetectionService, ILogger<DeviceViewModel> logger)
    {
        _deviceDetectionService = deviceDetectionService;
        _logger = logger;
        RefreshCommand = new AsyncCommand(RefreshAsync, () => !IsBusy);
        CancelCommand = new AsyncCommand(CancelRefreshAsync, () => IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<AndroidDevice> Devices { get; } = [];

    public bool HasDevices => Devices.Count > 0;

    public bool IsDeviceListEmpty => !HasDevices;

    public bool HasSelectedDevice => SelectedDevice is not null;

    public bool SelectedDeviceNeedsAuthorization => SelectedDevice?.AdbState == AdbDeviceState.Unauthorized;

    public bool IsSelectedDeviceAuthorized => SelectedDevice?.Authorized == true && AdbAvailable && !IsBusy;

    public bool IsAdbUnavailable => !AdbAvailable;

    public string DeviceCountLabel => Devices.Count == 1 ? "1 device detected" : $"{Devices.Count} devices detected";

    public string AdbAvailabilityLabel => AdbAvailable ? "ADB executable available" : "ADB executable unavailable";

    public string RecoveryAvailabilityMessage => SelectedDevice is null
        ? "Select a connected device before acquisition can be configured."
        : SelectedDevice.AdbState switch
        {
            AdbDeviceState.Unauthorized => "Unlock the phone and accept the USB debugging authorization prompt.",
            AdbDeviceState.Offline => "The selected device is offline. Check the USB connection and refresh devices.",
            AdbDeviceState.Device when !AdbAvailable => "ADB Platform Tools are unavailable on this PC.",
            AdbDeviceState.Device => "This authorized device is ready for read-only acquisition of accessible shared-storage files.",
            _ => "The device state is unknown; acquisition is unavailable."
        };

    public string SelectedDeviceName => SelectedDevice?.DisplayName ?? "No device selected";

    public string SelectedDeviceIdentity => string.IsNullOrWhiteSpace(SelectedDevice?.IdentitySummary)
        ? "Device details are not available yet"
        : SelectedDevice.IdentitySummary;

    public AsyncCommand RefreshCommand { get; }

    public AsyncCommand CancelCommand { get; }

    public AndroidDevice? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
            {
                OnPropertyChanged(nameof(SelectedManufacturer));
                OnPropertyChanged(nameof(SelectedModel));
                OnPropertyChanged(nameof(SelectedProduct));
                OnPropertyChanged(nameof(SelectedAndroidVersion));
                OnPropertyChanged(nameof(SelectedApiLevel));
                OnPropertyChanged(nameof(SelectedAdbState));
                OnPropertyChanged(nameof(SelectedAuthorizationState));
                OnPropertyChanged(nameof(SelectionHeading));
                OnPropertyChanged(nameof(HasSelectedDevice));
                OnPropertyChanged(nameof(SelectedDeviceNeedsAuthorization));
                OnPropertyChanged(nameof(IsSelectedDeviceAuthorized));
                OnPropertyChanged(nameof(SelectedDeviceName));
                OnPropertyChanged(nameof(SelectedDeviceIdentity));
                OnPropertyChanged(nameof(RecoveryAvailabilityMessage));
                OnPropertyChanged(nameof(AdbAvailabilityLabel));
            }
        }
    }

    public string SelectionHeading => SelectedDevice is null ? "No device selected" : "Selected device";

    public string SelectedManufacturer => ValueOrDash(SelectedDevice?.Manufacturer);

    public string SelectedModel => ValueOrDash(SelectedDevice?.Model);

    public string SelectedProduct => ValueOrDash(SelectedDevice?.Product);

    public string SelectedAndroidVersion => ValueOrDash(SelectedDevice?.AndroidVersion);

    public string SelectedApiLevel => SelectedDevice?.ApiLevel?.ToString() ?? "-";

    public string SelectedAdbState => SelectedDevice?.AdbStateLabel ?? "-";

    public string SelectedAuthorizationState => SelectedDevice?.AuthorizationStateLabel ?? "-";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshCommand.NotifyCanExecuteChanged();
                CancelCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(IsSelectedDeviceAuthorized));
            }
        }
    }

    public bool AdbAvailable
    {
        get => _adbAvailable;
        private set
        {
            if (SetProperty(ref _adbAvailable, value))
            {
                OnPropertyChanged(nameof(IsAdbUnavailable));
                OnPropertyChanged(nameof(AdbAvailabilityLabel));
                OnPropertyChanged(nameof(RecoveryAvailabilityMessage));
                OnPropertyChanged(nameof(IsSelectedDeviceAuthorized));
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string? TechnicalDetails
    {
        get => _technicalDetails;
        private set => SetProperty(ref _technicalDetails, value);
    }

    public string AdbExecutablePath
    {
        get => _adbExecutablePath;
        private set => SetProperty(ref _adbExecutablePath, value);
    }

    public async Task RefreshAsync()
    {
        _refreshCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        _refreshCancellation = cancellation;
        IsBusy = true;
        StatusMessage = "Checking connected Android devices...";
        TechnicalDetails = null;

        try
        {
            var result = await _deviceDetectionService.GetDevicesAsync(cancellation.Token);
            var selectedId = SelectedDevice?.Id;
            Devices.Clear();
            foreach (var device in result.Devices)
            {
                Devices.Add(device);
            }

            OnPropertyChanged(nameof(HasDevices));
            OnPropertyChanged(nameof(IsDeviceListEmpty));
            OnPropertyChanged(nameof(DeviceCountLabel));

            SelectedDevice = selectedId is null
                ? null
                : Devices.FirstOrDefault(device => string.Equals(device.Id, selectedId, StringComparison.Ordinal));

            AdbAvailable = result.AdbAvailable;
            AdbExecutablePath = result.AdbExecutablePath ?? "Not resolved";
            StatusMessage = result.Message;
            TechnicalDetails = result.TechnicalDetails;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            StatusMessage = "Device detection was cancelled.";
        }
        catch (Exception exception)
        {
            _logger.LogError("Device detection failed ({ExceptionType})", exception.GetType().Name);
            StatusMessage = "Device detection failed. Check the ADB installation and try again.";
            TechnicalDetails = "An unexpected error occurred while communicating with ADB.";
        }
        finally
        {
            if (ReferenceEquals(_refreshCancellation, cancellation))
            {
                _refreshCancellation = null;
            }

            IsBusy = false;
        }
    }

    private Task CancelRefreshAsync()
    {
        _refreshCancellation?.Cancel();
        return Task.CompletedTask;
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName!);
        return true;
    }

    private static string ValueOrDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value;
}