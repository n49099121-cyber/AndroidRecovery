namespace AndroidRecovery.Models;

public enum AdbDeviceState
{
    Device,
    Unauthorized,
    Offline,
    NoPermissions,
    Unknown
}

public enum DeviceConnectionState
{
    Connected,
    Unauthorized,
    Offline,
    Unknown
}

public sealed record AndroidDevice(
    string Id,
    string Manufacturer,
    string Model,
    string Product,
    string AndroidVersion,
    int? ApiLevel,
    AdbDeviceState AdbState)
{
    public bool Authorized => AdbState == AdbDeviceState.Device;

    public DeviceConnectionState ConnectionState => AdbState switch
    {
        AdbDeviceState.Device => DeviceConnectionState.Connected,
        AdbDeviceState.Unauthorized => DeviceConnectionState.Unauthorized,
        AdbDeviceState.Offline => DeviceConnectionState.Offline,
        _ => DeviceConnectionState.Unknown
    };

    public string DisplayName => string.IsNullOrWhiteSpace(Model) ? "Android device" : Model;

    public string IdentitySummary => string.Join(" · ", new[]
    {
        Manufacturer,
        string.IsNullOrWhiteSpace(AndroidVersion) ? "" : $"Android {AndroidVersion}",
        ApiLevel is null ? "" : $"API {ApiLevel}"
    }.Where(value => !string.IsNullOrWhiteSpace(value)));

    public string AdbStateLabel => AdbState switch
    {
        AdbDeviceState.Device => "ADB connected",
        AdbDeviceState.Unauthorized => "ADB authorization required",
        AdbDeviceState.Offline => "ADB offline",
        AdbDeviceState.NoPermissions => "USB permissions unavailable",
        _ => "ADB state unknown"
    };

    public string AuthorizationStateLabel => Authorized
        ? "Authorized"
        : AdbState == AdbDeviceState.Unauthorized ? "Not authorized on phone" : "Not confirmed";
}