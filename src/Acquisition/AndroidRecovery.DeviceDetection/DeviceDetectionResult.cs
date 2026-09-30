using AndroidRecovery.Models;

namespace AndroidRecovery.DeviceDetection;

public sealed record DeviceDetectionResult(
    bool AdbAvailable,
    IReadOnlyList<AndroidDevice> Devices,
    string Message,
    string? TechnicalDetails = null,
    string? AdbExecutablePath = null);