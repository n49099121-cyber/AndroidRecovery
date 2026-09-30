using System.Diagnostics;
using System.Text;
using AndroidRecovery.Models;
using AndroidRecovery.Recovery;
using Microsoft.Extensions.Logging;

namespace AndroidRecovery.Adb;

public sealed class AdbDeviceFileEnumerator : IDeviceFileEnumerator
{
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(150);
    private readonly IAdbService _adbService;
    private readonly ILogger<AdbDeviceFileEnumerator> _logger;

    public AdbDeviceFileEnumerator(IAdbService adbService, ILogger<AdbDeviceFileEnumerator> logger)
    {
        _adbService = adbService;
        _logger = logger;
    }

    public async Task<FileEnumerationResult> EnumerateAsync(
        AndroidDevice device,
        IReadOnlySet<AcquisitionCategory> categories,
        IProgress<AcquisitionProgress>? progress,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var files = new List<DeviceFile>();
        var errors = new List<AcquisitionError>();
        var warnings = new List<AcquisitionError>();
        if (!device.Authorized)
        {
            errors.Add(CreateError("ValidateDevice", "DeviceNotAuthorized",
                "The device is not authorized for ADB access.", AcquisitionErrorSeverity.FatalError, false));
            return new(files, errors, warnings);
        }

        var roots = CombineRoots(categories);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var bytesDiscovered = 0L;
        var lastReport = Stopwatch.GetTimestamp();
        var disconnected = false;

        foreach (var (directoryName, extensions) in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var androidRoot = $"/sdcard/{directoryName}";
            _logger.LogInformation("ADB file enumeration started for session {SessionId} at {SourceRoot}",
                sessionId, androidRoot);

            try
            {
                var pendingPathBytes = new List<byte>(256);
                var skippingOversizedPath = false;
                var findResult = await _adbService.ExecuteBytesAsync(
                    ["-s", device.Id, "shell", "find", AndroidShellPath.Quote(androidRoot), "-type", "f", "-print0"],
                    async (chunk, token) =>
                    {
                        foreach (var value in chunk.ToArray())
                        {
                            token.ThrowIfCancellationRequested();
                            if (value == 0)
                            {
                                if (!skippingOversizedPath && pendingPathBytes.Count > 0)
                                {
                                    var decodedPath = new UTF8Encoding(false, true).GetString(pendingPathBytes.ToArray());
                                    await ProcessPathAsync(decodedPath, androidRoot, extensions, device, sessionId,
                                        seen, files, warnings, errors, progress,
                                        () => bytesDiscovered, value => bytesDiscovered = value,
                                        value => disconnected = value,
                                        () => lastReport, value => lastReport = value, token).ConfigureAwait(false);
                                }

                                pendingPathBytes.Clear();
                                skippingOversizedPath = false;
                            }
                            else if (!skippingOversizedPath)
                            {
                                if (pendingPathBytes.Count >= 32 * 1024)
                                {
                                    skippingOversizedPath = true;
                                    pendingPathBytes.Clear();
                                }
                                else
                                {
                                    pendingPathBytes.Add(value);
                                }
                            }
                        }
                    }, cancellationToken).ConfigureAwait(false);

                if (pendingPathBytes.Count > 0 || skippingOversizedPath)
                {
                    warnings.Add(CreateError("EnumerateFiles", "MalformedFindOutput",
                        "ADB returned an unterminated or oversized file path; it was skipped.",
                        AcquisitionErrorSeverity.Warning, true, androidRoot));
                }

                if (!findResult.Succeeded && !disconnected)
                {
                    if (IsDisconnected(findResult))
                    {
                        disconnected = true;
                        errors.Add(CreateError("EnumerateFiles", "DeviceDisconnected",
                            "The Android device disconnected during file enumeration.",
                            AcquisitionErrorSeverity.FatalError, false, androidRoot));
                        break;
                    }

                    if (IsMissingDirectory(findResult))
                    {
                        warnings.Add(CreateError("EnumerateFiles", "DirectoryUnavailable",
                            $"The shared-storage directory {directoryName} is not present or cannot be listed.",
                            AcquisitionErrorSeverity.Warning, true, androidRoot));
                    }
                    else
                    {
                        errors.Add(CreateError("EnumerateFiles", findResult.ErrorCode ?? "EnumerationFailed",
                            $"ADB could not enumerate the shared-storage directory {directoryName}.",
                            AcquisitionErrorSeverity.FatalError, false, androidRoot));
                        break;
                    }
                }
            }
            catch (EnumerationStoppedException)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError("ADB enumeration failed for session {SessionId} with {ExceptionType}",
                    sessionId, exception.GetType().Name);
                errors.Add(CreateError("EnumerateFiles", "EnumerationFailed",
                    "An unexpected error interrupted file enumeration.", AcquisitionErrorSeverity.FatalError,
                    false, androidRoot, exception.GetType().Name));
                break;
            }
        }

        return new(files, errors, warnings, disconnected);
    }

    private static Dictionary<string, IReadOnlySet<string>?> CombineRoots(IReadOnlySet<AcquisitionCategory> categories)
    {
        var roots = new Dictionary<string, IReadOnlySet<string>?>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in categories)
        {
            foreach (var source in AcquisitionCategorySourceMap.GetSources(category))
            {
                if (!roots.TryGetValue(source.DirectoryName, out var existing))
                {
                    roots.Add(source.DirectoryName, source.Extensions);
                }
                else if (existing is null || source.Extensions is null)
                {
                    roots[source.DirectoryName] = null;
                }
                else
                {
                    roots[source.DirectoryName] = new HashSet<string>(existing.Concat(source.Extensions),
                        StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        return roots;
    }

    private async Task ProcessPathAsync(
        string line,
        string androidRoot,
        IReadOnlySet<string>? extensions,
        AndroidDevice device,
        string sessionId,
        HashSet<string> seen,
        List<DeviceFile> files,
        List<AcquisitionError> warnings,
        List<AcquisitionError> errors,
        IProgress<AcquisitionProgress>? progress,
        Func<long> getBytesDiscovered,
        Action<long> setBytesDiscovered,
        Action<bool> setDisconnected,
        Func<long> getLastReport,
        Action<long> setLastReport,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        if (!line.StartsWith(androidRoot + "/", StringComparison.Ordinal))
        {
            warnings.Add(CreateError("EnumerateFiles", "UnexpectedDevicePath",
                "ADB returned a path outside the requested shared-storage directory.",
                AcquisitionErrorSeverity.Warning, true));
            return;
        }

        var extension = Path.GetExtension(line);
        if (extensions is not null && !extensions.Contains(extension))
        {
            return;
        }

        if (!seen.Add(line))
        {
            return;
        }

        string relativePath;
        try
        {
            relativePath = EvidencePathUtility.ToEvidenceRelativePath(line);
        }
        catch (ArgumentException)
        {
            warnings.Add(CreateError("NormalizeDevicePath", "InvalidDevicePath",
                "A device path contained unsupported path segments and was skipped.",
                AcquisitionErrorSeverity.Warning, true));
            return;
        }

        var statResult = await _adbService.ExecuteAsync(
            ["-s", device.Id, "shell", "stat", "-c", "%s", "--", AndroidShellPath.Quote(line)], cancellationToken)
            .ConfigureAwait(false);
        long? fileSize = null;
        if (statResult.Succeeded && long.TryParse(statResult.StandardOutput.Trim(), out var parsedSize)
            && parsedSize >= 0)
        {
            fileSize = parsedSize;
            setBytesDiscovered(SaturatingAdd(getBytesDiscovered(), parsedSize));
        }
        else if (IsDisconnected(statResult))
        {
            setDisconnected(true);
            errors.Add(CreateError("ReadFileMetadata", "DeviceDisconnected",
                "The Android device disconnected during file enumeration.",
                AcquisitionErrorSeverity.FatalError, false, line));
            throw new EnumerationStoppedException();
        }
        else
        {
            warnings.Add(CreateError("ReadFileMetadata", "FileSizeUnavailable",
                "File size could not be read; the file may still be acquired.",
                AcquisitionErrorSeverity.Warning, true, line));
        }

        files.Add(new DeviceFile(line, relativePath, fileSize));
        var now = Stopwatch.GetTimestamp();
        if (files.Count % 32 == 0 || Stopwatch.GetElapsedTime(getLastReport(), now) >= ProgressInterval)
        {
            setLastReport(now);
            progress?.Report(new AcquisitionProgress
            {
                SessionId = sessionId,
                Status = AcquisitionStatus.Preparing,
                FilesDiscovered = files.Count,
                BytesDiscovered = getBytesDiscovered(),
                IsByteTotalReliable = false,
                CurrentSourcePath = line,
                ErrorCount = errors.Count,
                WarningCount = warnings.Count
            });
        }
    }

    private static bool IsMissingDirectory(AdbCommandResult result) =>
        result.StandardError.Contains("no such file or directory", StringComparison.OrdinalIgnoreCase)
        || result.StandardError.Contains("not found", StringComparison.OrdinalIgnoreCase)
        || result.StandardError.Contains("permission denied", StringComparison.OrdinalIgnoreCase)
        || result.StandardError.Contains("operation not permitted", StringComparison.OrdinalIgnoreCase);

    private static bool IsDisconnected(AdbCommandResult result)
    {
        var error = result.StandardError;
        return error.Contains("device not found", StringComparison.OrdinalIgnoreCase)
            || error.Contains("device offline", StringComparison.OrdinalIgnoreCase)
            || error.Contains("device disconnected", StringComparison.OrdinalIgnoreCase)
            || error.Contains("no devices/emulators found", StringComparison.OrdinalIgnoreCase)
            || error.Contains("transport error", StringComparison.OrdinalIgnoreCase);
    }

    private static AcquisitionError CreateError(
        string operation,
        string errorCode,
        string message,
        AcquisitionErrorSeverity severity,
        bool recoverable,
        string? sourcePath = null,
        string? exceptionType = null) => new()
        {
            Operation = operation,
            ErrorCode = errorCode,
            Message = message,
            Severity = severity,
            IsRecoverable = recoverable,
            SourcePath = sourcePath,
            ExceptionType = exceptionType
        };

    private static long SaturatingAdd(long current, long addition) =>
        current > long.MaxValue - addition ? long.MaxValue : current + addition;

    private sealed class EnumerationStoppedException : Exception;
}