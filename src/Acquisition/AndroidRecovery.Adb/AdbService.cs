using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;

namespace AndroidRecovery.Adb;

public sealed class AdbService : IAdbService
{
    private readonly AdbOptions _options;

    public AdbService(IOptions<AdbOptions> options)
    {
        _options = options.Value;
        ExecutablePath = Path.GetFullPath(Path.IsPathRooted(_options.ExecutablePath)
            ? _options.ExecutablePath
            : Path.Combine(AppContext.BaseDirectory, _options.ExecutablePath));
    }

    public bool IsAvailable => File.Exists(ExecutablePath);

    public string ExecutablePath { get; }

    public async Task<AdbCommandResult> ExecuteAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        return await ExecuteCoreAsync(arguments, _options.TimeoutSeconds,
            (process, _, _) => process.StandardOutput.ReadToEndAsync(), null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AdbCommandResult> ExecuteLinesAsync(
        IReadOnlyList<string> arguments,
        Func<string, CancellationToken, ValueTask> onLine,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onLine);
        return await ExecuteCoreAsync(arguments, _options.EnumerationTimeoutSeconds,
            ReadLinesAsync, onLine, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AdbCommandResult> ExecuteBytesAsync(
        IReadOnlyList<string> arguments,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> onBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onBytes);
        return await ExecuteCoreAsync(arguments, _options.EnumerationTimeoutSeconds,
            ReadBytesAsync, onBytes, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AdbCommandResult> PullFileAsync(
        string deviceSerial,
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceSerial))
        {
            throw new ArgumentException("An authorized device serial is required.", nameof(deviceSerial));
        }

        if (string.IsNullOrWhiteSpace(sourcePath) || !sourcePath.StartsWith("/", StringComparison.Ordinal))
        {
            throw new ArgumentException("The ADB source must be an absolute Android path.", nameof(sourcePath));
        }

        if (string.IsNullOrWhiteSpace(destinationPath) || !Path.IsPathFullyQualified(destinationPath))
        {
            throw new ArgumentException("The ADB destination must be an absolute Windows path.", nameof(destinationPath));
        }

        var arguments = new[] { "-s", deviceSerial, "pull", sourcePath, Path.GetFullPath(destinationPath) };
        return await ExecuteCoreAsync(arguments, _options.TransferTimeoutSeconds,
            (process, _, _) => process.StandardOutput.ReadToEndAsync(), null, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AdbCommandResult> ExecuteCoreAsync(
        IReadOnlyList<string> arguments,
        int timeoutSeconds,
        Func<Process, CancellationToken, Delegate?, Task<string>> readOutput,
        Delegate? outputHandler,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsAvailable)
        {
            return new(false, "", "The configured app-local ADB executable was not found.", null, "AdbUnavailable");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return new(false, "", "ADB did not start.", null, "AdbStartFailed");
            }

            using var timeoutSource = new CancellationTokenSource(
                TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 7200)));
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutSource.Token);
            var outputTask = readOutput(process, linkedSource.Token, outputHandler);
            var errorTask = process.StandardError.ReadToEndAsync();
            var waitTask = process.WaitForExitAsync(linkedSource.Token);
            var firstTask = await Task.WhenAny(outputTask, waitTask).ConfigureAwait(false);
            if (firstTask == outputTask && outputTask.IsFaulted)
            {
                TryKill(process);
                await IgnoreFailureAsync(waitTask).ConfigureAwait(false);
                await errorTask.ConfigureAwait(false);
                await outputTask.ConfigureAwait(false);
            }

            try
            {
                await waitTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                await IgnoreFailureAsync(outputTask).ConfigureAwait(false);
                await IgnoreFailureAsync(errorTask).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                return new(false, "", "ADB did not respond before the timeout.", null, "AdbTimeout");
            }

            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            return new(process.ExitCode == 0, output, error, process.ExitCode,
                process.ExitCode == 0 ? null : "AdbCommandFailed");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Win32Exception)
        {
            return new(false, "", "Windows could not start the configured ADB executable.", null, "AdbStartFailed");
        }
        catch (InvalidOperationException)
        {
            return new(false, "", "ADB could not be started.", null, "AdbStartFailed");
        }
        catch (IOException)
        {
            return new(false, "", "ADB could not be started or its output could not be read.", null, "AdbIoError");
        }
        catch
        {
            TryKill(process);
            throw;
        }
    }

    private static async Task<string> ReadLinesAsync(
        Process process,
        CancellationToken cancellationToken,
        Delegate? outputHandler)
    {
        if (outputHandler is not Func<string, CancellationToken, ValueTask> onLine)
        {
            throw new ArgumentNullException(nameof(onLine));
        }

        while (await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            await onLine(line, cancellationToken).ConfigureAwait(false);
        }

        return "";
    }

    private static async Task<string> ReadBytesAsync(
        Process process,
        CancellationToken cancellationToken,
        Delegate? outputHandler)
    {
        if (outputHandler is not Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> onBytes)
        {
            throw new ArgumentNullException(nameof(outputHandler));
        }

        var buffer = new byte[32 * 1024];
        while (true)
        {
            var count = await process.StandardOutput.BaseStream.ReadAsync(buffer.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
            if (count == 0)
            {
                break;
            }

            await onBytes(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
        }

        return "";
    }

    private static async Task IgnoreFailureAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task IgnoreFailureAsync<T>(Task<T> task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (AggregateException)
        {
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
    }
}