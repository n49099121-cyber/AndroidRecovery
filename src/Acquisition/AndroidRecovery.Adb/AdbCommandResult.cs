namespace AndroidRecovery.Adb;

public sealed record AdbCommandResult(
    bool Succeeded,
    string StandardOutput,
    string StandardError,
    int? ExitCode,
    string? ErrorCode = null);