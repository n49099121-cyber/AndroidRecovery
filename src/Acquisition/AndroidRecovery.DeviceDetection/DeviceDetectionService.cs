using AndroidRecovery.Adb;
using AndroidRecovery.Models;
using Microsoft.Extensions.Logging;

namespace AndroidRecovery.DeviceDetection;

public sealed class DeviceDetectionService : IDeviceDetectionService
{
    private readonly IAdbService _adbService;
    private readonly ILogger<DeviceDetectionService> _logger;

    public DeviceDetectionService(IAdbService adbService, ILogger<DeviceDetectionService> logger)
    {
        _adbService = adbService;
        _logger = logger;
    }

    public async Task<DeviceDetectionResult> GetDevicesAsync(CancellationToken cancellationToken)
    {
        if (!_adbService.IsAvailable)
        {
            return new(false, [],
                "ADB is unavailable. Add the trusted Android SDK Platform Tools executable at tools/platform-tools/adb.exe and restart the app.",
                $"Expected executable: {_adbService.ExecutablePath}",
                _adbService.ExecutablePath);
        }

        var listResult = await _adbService.ExecuteAsync(["devices", "-l"], cancellationToken).ConfigureAwait(false);
        if (!listResult.Succeeded)
        {
            _logger.LogWarning("ADB device enumeration failed with code {ErrorCode}", listResult.ErrorCode);
            return new(true, [], "Android Debug Bridge could not enumerate devices. Check the USB connection and ADB installation.",
                listResult.StandardError.Trim(), _adbService.ExecutablePath);
        }

        var parsed = AdbDeviceListParser.Parse(listResult.StandardOutput);
        if (parsed.MalformedLineCount > 0)
        {
            _logger.LogWarning("ADB returned {MalformedLineCount} malformed device-list line(s)", parsed.MalformedLineCount);
        }

        var devices = new List<AndroidDevice>(parsed.Devices.Count);
        foreach (var entry in parsed.Devices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manufacturer = "";
            var model = entry.Model;
            var product = entry.Product;
            var androidVersion = "";
            int? apiLevel = null;

            if (entry.State == AdbDeviceState.Device)
            {
                var propertiesResult = await _adbService.ExecuteAsync(
                    ["-s", entry.Id, "shell", "getprop"], cancellationToken).ConfigureAwait(false);
                if (propertiesResult.Succeeded)
                {
                    var properties = ParseProperties(propertiesResult.StandardOutput);
                    manufacturer = Get(properties, "ro.product.manufacturer");
                    model = FirstNonEmpty(Get(properties, "ro.product.model"), model);
                    product = FirstNonEmpty(Get(properties, "ro.product.name"), product);
                    androidVersion = Get(properties, "ro.build.version.release");
                    if (int.TryParse(Get(properties, "ro.build.version.sdk"), out var parsedApiLevel))
                    {
                        apiLevel = parsedApiLevel;
                    }
                }
                else
                {
                    _logger.LogInformation("ADB device properties were unavailable for one connected device ({ErrorCode})",
                        propertiesResult.ErrorCode);
                }
            }

            devices.Add(new AndroidDevice(entry.Id, manufacturer, model, product, androidVersion, apiLevel, entry.State));
        }

        var message = devices.Count == 0
            ? "No Android devices detected. Connect a phone with USB debugging enabled."
            : $"Detected {devices.Count} Android device(s). Select a device to view its details.";
        return new(true, devices, message,
            parsed.MalformedLineCount > 0 ? $"Ignored {parsed.MalformedLineCount} malformed ADB response line(s)." : null,
            _adbService.ExecutablePath);
    }

    private static Dictionary<string, string> ParseProperties(string output)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith('['))
            {
                continue;
            }

            var separator = line.IndexOf("]: [", StringComparison.Ordinal);
            if (separator <= 1 || !line.EndsWith(']'))
            {
                continue;
            }

            properties[line[1..separator]] = line[(separator + 4)..^1];
        }

        return properties;
    }

    private static string Get(IReadOnlyDictionary<string, string> properties, string key) =>
        properties.TryGetValue(key, out var value) ? value : "";

    private static string FirstNonEmpty(string preferred, string fallback) =>
        string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;
}