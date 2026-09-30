namespace AndroidRecovery.Adb;

public sealed class AdbOptions
{
    public string ExecutablePath { get; set; } = "tools/platform-tools/adb.exe";

    public int TimeoutSeconds { get; set; } = 8;

    public int EnumerationTimeoutSeconds { get; set; } = 1800;

    public int TransferTimeoutSeconds { get; set; } = 3600;
}