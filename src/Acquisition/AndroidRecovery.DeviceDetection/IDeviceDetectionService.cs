namespace AndroidRecovery.DeviceDetection;

public interface IDeviceDetectionService
{
    Task<DeviceDetectionResult> GetDevicesAsync(CancellationToken cancellationToken);
}