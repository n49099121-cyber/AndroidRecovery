namespace AndroidRecovery.Adb;

public interface IAdbService
{
    bool IsAvailable { get; }

    string ExecutablePath { get; }

    Task<AdbCommandResult> ExecuteAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken);

    Task<AdbCommandResult> ExecuteLinesAsync(
        IReadOnlyList<string> arguments,
        Func<string, CancellationToken, ValueTask> onLine,
        CancellationToken cancellationToken);

    Task<AdbCommandResult> ExecuteBytesAsync(
        IReadOnlyList<string> arguments,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> onBytes,
        CancellationToken cancellationToken);

    Task<AdbCommandResult> PullFileAsync(
        string deviceSerial,
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken);
}