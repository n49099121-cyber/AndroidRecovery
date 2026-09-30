using AndroidRecovery.Adb;
using AndroidRecovery.DeviceDetection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AndroidRecovery.IntegrationTests;

public sealed class DeviceDetectionTests
{
    [Fact]
    public async Task GetDevicesAsync_WhenAdbIsMissing_ReturnsActionableStatus()
    {
        var adb = new AdbService(Options.Create(new AdbOptions
        {
            ExecutablePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "adb.exe")
        }));
        var service = new DeviceDetectionService(adb, NullLogger<DeviceDetectionService>.Instance);

        var result = await service.GetDevicesAsync(CancellationToken.None);

        Assert.False(result.AdbAvailable);
        Assert.Empty(result.Devices);
        Assert.Contains("Platform Tools", result.Message);
        Assert.Equal(adb.ExecutablePath, result.AdbExecutablePath);
    }
}