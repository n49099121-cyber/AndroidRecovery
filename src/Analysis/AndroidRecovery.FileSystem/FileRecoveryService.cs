using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using AndroidRecovery.Core;
using AndroidRecovery.Models;
using AndroidRecovery.Recovery;
using Microsoft.Extensions.Logging;

namespace AndroidRecovery.FileSystem;

public sealed class FileRecoveryService : IRecoveryService
{
    private const int CopyBufferSize = 128 * 1024;
    private const long MinimumFreeSpaceReserveBytes = 512L * 1024 * 1024;
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(200);
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly IHashService _hashService;
    private readonly ILogger<FileRecoveryService> _logger;
    private readonly long _minimumFreeSpaceReserveBytes;

    public FileRecoveryService(
        IHashService hashService,
        ILogger<FileRecoveryService> logger,
        long minimumFreeSpaceReserveBytes = MinimumFreeSpaceReserveBytes)
    {
        if (minimumFreeSpaceReserveBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumFreeSpaceReserveBytes));
        }

        _hashService = hashService;
        _logger = logger;
        _minimumFreeSpaceReserveBytes = minimumFreeSpaceReserveBytes;
    }

    public async Task<RecoveryResult> ExportAsync(
        string evidencePackagePath,
        IReadOnlyList<AnalyzedEvidenceItem> items,
        string destinationPath,
        IProgress<RecoveryProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evidencePackagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(items);
        cancellationToken.ThrowIfCancellationRequested();

        var startedUtc = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var sessionId = $"RR-{startedUtc:yyyyMMdd}-{Guid.NewGuid():N}";
        var packageRoot = Path.GetFullPath(evidencePackagePath);
        var destinationRoot = Path.GetFullPath(destinationPath);
        var selected = items.GroupBy(item => item.Id, StringComparer.Ordinal).Select(group => group.First()).ToArray();
        var files = new List<RecoveryFileResult>(selected.Length);
        var errors = new List<string>();
        var warnings = new List<string>();
        var outputFolder = "";
        var acquisitionSessionId = "";
        var deviceSerial = "";
        var deviceManufacturer = "";
        var deviceModel = "";

        if (selected.Length == 0)
        {
            errors.Add("Select at least one analyzed evidence file to export.");
            return CreateResult(RecoverySessionStatus.Failed);
        }

        if (IsPathWithinRoot(packageRoot, destinationRoot))
        {
            errors.Add("Choose an export destination outside the source evidence package. The evidence package will not be modified.");
            return CreateResult(RecoverySessionStatus.Failed);
        }

        try
        {
            var metadataPath = Path.Combine(packageRoot, "metadata.json");
            await using (var metadataStream = File.OpenRead(metadataPath))
            {
                var metadata = await JsonSerializer.DeserializeAsync<EvidenceMetadata>(metadataStream, JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                if (metadata?.Session is null || string.IsNullOrWhiteSpace(metadata.Session.SessionId))
                {
                    throw new InvalidDataException("Evidence metadata does not contain a valid acquisition session.");
                }

                acquisitionSessionId = metadata.Session.SessionId;
                deviceSerial = metadata.Session.DeviceSerial;
                deviceManufacturer = metadata.Session.DeviceManufacturer;
                deviceModel = metadata.Session.DeviceModel;
            }

            Directory.CreateDirectory(destinationRoot);
            await ValidateWritableAsync(destinationRoot, cancellationToken).ConfigureAwait(false);
            var requiredBytes = SumSizes(selected);
            var availableBytes = new DriveInfo(Path.GetPathRoot(destinationRoot)!).AvailableFreeSpace;
            if (SaturatingAdd(requiredBytes, _minimumFreeSpaceReserveBytes) > availableBytes)
            {
                errors.Add($"Not enough destination space. Required: {requiredBytes:N0} bytes plus {_minimumFreeSpaceReserveBytes:N0} bytes of safety reserve; available: {availableBytes:N0} bytes.");
                return CreateResult(RecoverySessionStatus.Failed);
            }

            outputFolder = CreateUniqueOutputFolder(destinationRoot, startedUtc, sessionId);
            var outputFilesRoot = Path.Combine(outputFolder, "files");
            Directory.CreateDirectory(outputFilesRoot);
            var reservedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long bytesCompleted = 0;
            long filesCompleted = 0;
            var lastReport = Stopwatch.GetTimestamp();
            _logger.LogInformation("RecoveryExportStarted for session {SessionId} with {FileCount} files and {Bytes} bytes",
                sessionId, selected.Length, requiredBytes);

            for (var itemIndex = 0; itemIndex < selected.Length; itemIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = selected[itemIndex];
                string? temporaryPath = null;
                string? targetPath = null;
                try
                {
                    if (item.IsCorrupt == true || string.IsNullOrWhiteSpace(item.Sha256))
                    {
                        throw new InvalidDataException("This item is corrupt or has no trusted source SHA-256 and cannot be exported as verified.");
                    }

                    var sourcePath = EvidencePreviewService.ResolveEvidenceFile(packageRoot, item.RelativePath);
                    var sourceInfo = new FileInfo(sourcePath);
                    if (sourceInfo.Length != item.SizeBytes)
                    {
                        throw new InvalidDataException("The evidence file size does not match the analyzed result.");
                    }

                    targetPath = ReserveOutputPath(outputFilesRoot, item.RelativePath, reservedPaths);
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                    temporaryPath = targetPath + ".partial-" + Guid.NewGuid().ToString("N");
                    var sourceHash = await CopyAndHashAsync(sourcePath, temporaryPath, item, sessionId,
                        filesCompleted, selected.Length, bytesCompleted, requiredBytes, progress, stopwatch,
                        () => lastReport, value => lastReport = value, cancellationToken).ConfigureAwait(false);
                    if (!sourceHash.Equals(item.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("The evidence source SHA-256 no longer matches its verified analysis record.");
                    }

                    var destinationHash = await _hashService.CalculateSha256Async(temporaryPath, cancellationToken)
                        .ConfigureAwait(false);
                    if (!destinationHash.Equals(sourceHash, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("The exported file SHA-256 does not match the evidence source.");
                    }

                    File.Move(temporaryPath, targetPath, overwrite: false);
                    temporaryPath = null;
                    files.Add(new(item.Id, item.SourcePath, targetPath, sourceInfo.Length, sourceHash,
                        destinationHash, RecoveryFileStatus.Verified, null));
                    bytesCompleted = SaturatingAdd(bytesCompleted, sourceInfo.Length);
                    filesCompleted++;
                    _logger.LogInformation("RecoveryFileExported and verified for session {SessionId} ({Bytes} bytes)",
                        sessionId, sourceInfo.Length);
                }
                catch (OperationCanceledException)
                {
                    TryDelete(temporaryPath);
                    throw;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                    or ArgumentException or InvalidDataException or NotSupportedException)
                {
                    TryDelete(temporaryPath);
                    files.Add(new(item.Id, item.SourcePath, targetPath ?? "", item.SizeBytes,
                        item.Sha256, null, RecoveryFileStatus.Failed, UserSafeFileError(exception)));
                    errors.Add($"{item.FileName}: {UserSafeFileError(exception)}");
                    _logger.LogWarning("RecoveryFileFailed for session {SessionId} ({ExceptionType})",
                        sessionId, exception.GetType().Name);
                }
            }

            var status = files.Any(file => file.Status == RecoveryFileStatus.Failed)
                ? RecoverySessionStatus.CompletedWithWarnings
                : RecoverySessionStatus.Completed;
            var result = CreateResult(status);
            await WriteReportAsync(result, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("RecoveryExportCompleted for session {SessionId} with {Completed} verified files and {Failed} failures",
                sessionId, result.SuccessfulCount, result.FailedCount);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            foreach (var item in selected.Skip(files.Count))
            {
                files.Add(new(item.Id, item.SourcePath, "", item.SizeBytes, item.Sha256, null,
                    RecoveryFileStatus.Cancelled, "Export cancelled before this file was copied."));
            }

            warnings.Add("Export cancelled. Already exported and verified files were preserved.");
            var result = CreateResult(RecoverySessionStatus.Cancelled);
            if (!string.IsNullOrWhiteSpace(outputFolder))
            {
                try
                {
                    await WriteReportAsync(result, CancellationToken.None).ConfigureAwait(false);
                }
                catch (IOException)
                {
                }
            }

            _logger.LogInformation("RecoveryExportCancelled for session {SessionId} with {Completed} verified files",
                sessionId, result.SuccessfulCount);
            return result;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or JsonException)
        {
            errors.Add($"Export could not start: {UserSafeFileError(exception)}");
            _logger.LogError("RecoveryExportFailed for session {SessionId} ({ExceptionType})",
                sessionId, exception.GetType().Name);
            return CreateResult(RecoverySessionStatus.Failed);
        }

        RecoveryResult CreateResult(RecoverySessionStatus status) => new(
            sessionId, startedUtc, DateTimeOffset.UtcNow, packageRoot, destinationRoot,
            outputFolder, status, files.ToArray(), warnings.ToArray(), errors.ToArray())
        {
            SelectedCount = selected.Length,
            AcquisitionSessionId = acquisitionSessionId,
            DeviceSerial = deviceSerial,
            DeviceManufacturer = deviceManufacturer,
            DeviceModel = deviceModel
        };

        async Task WriteReportAsync(RecoveryResult result, CancellationToken token)
        {
            var reportPath = Path.Combine(outputFolder, "recovery-report.json");
            var temporaryReportPath = reportPath + ".tmp";
            await using (var stream = new FileStream(temporaryReportPath, FileMode.Create, FileAccess.Write,
                FileShare.None, 32 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var report = new
                {
                    application = new
                    {
                        name = "AndroidRecovery",
                        version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "Unknown"
                    },
                    recovery = result
                };
                await JsonSerializer.SerializeAsync(stream, report, JsonOptions, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
            }

            File.Move(temporaryReportPath, reportPath, overwrite: true);
        }
    }

    private static async Task<string> CopyAndHashAsync(
        string sourcePath,
        string temporaryPath,
        AnalyzedEvidenceItem item,
        string sessionId,
        long filesCompleted,
        long filesTotal,
        long bytesCompleted,
        long bytesTotal,
        IProgress<RecoveryProgress>? progress,
        Stopwatch stopwatch,
        Func<long> getLastReport,
        Action<long> setLastReport,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[CopyBufferSize];
        long fileBytesCopied = 0;
        try
        {
            await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                CopyBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, CopyBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            while (true)
            {
                var bytesRead = await source.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break;
                }

                hash.AppendData(buffer, 0, bytesRead);
                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                fileBytesCopied += bytesRead;
                var now = Stopwatch.GetTimestamp();
                if (Stopwatch.GetElapsedTime(getLastReport(), now) >= ProgressInterval)
                {
                    setLastReport(now);
                    var elapsed = stopwatch.Elapsed;
                    var totalBytes = SaturatingAdd(bytesCompleted, fileBytesCopied);
                    var remaining = totalBytes > 0 && bytesTotal >= totalBytes
                        ? TimeSpan.FromTicks((long)(elapsed.Ticks * ((double)(bytesTotal - totalBytes) / totalBytes)))
                        : (TimeSpan?)null;
                    progress?.Report(new(sessionId, filesCompleted, filesTotal, totalBytes, bytesTotal,
                        item.RelativePath, elapsed, remaining));
                }
            }

            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    private static string ReserveOutputPath(string outputFilesRoot, string relativePath, HashSet<string> reserved)
    {
        var normalized = EvidencePathUtility.NormalizeRelativePath(relativePath);
        var directory = Path.GetDirectoryName(normalized) ?? "";
        var fileName = Path.GetFileNameWithoutExtension(normalized);
        var extension = Path.GetExtension(normalized);
        for (var index = 0; ; index++)
        {
            var name = index == 0 ? fileName : $"{fileName} ({index})";
            var candidate = string.IsNullOrEmpty(directory)
                ? name + extension
                : Path.Combine(directory, name + extension);
            var path = EvidencePathUtility.ResolveInsideRoot(outputFilesRoot, candidate);
            if (reserved.Add(candidate) && !File.Exists(path) && !Directory.Exists(path))
            {
                return path;
            }
        }
    }

    private static string CreateUniqueOutputFolder(string destinationRoot, DateTimeOffset timestamp, string sessionId)
    {
        var suffix = sessionId[^8..];
        for (var attempt = 0; attempt < 1000; attempt++)
        {
            var name = $"AndroidRecovery_Recovery_{timestamp:yyyyMMdd_HHmmss}_{suffix}"
                + (attempt == 0 ? "" : $"_{attempt}");
            var path = Path.Combine(destinationRoot, name);
            if (Directory.Exists(path) || File.Exists(path))
            {
                continue;
            }

            Directory.CreateDirectory(path);
            return path;
        }

        throw new IOException("A unique recovery output folder could not be created.");
    }

    private static async Task ValidateWritableAsync(string destinationRoot, CancellationToken cancellationToken)
    {
        var probePath = Path.Combine(destinationRoot, $".androidrecovery-write-test-{Guid.NewGuid():N}");
        try
        {
            await using var stream = new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                1, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            await stream.WriteAsync(new byte[] { 0 }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDelete(probePath);
        }
    }

    private static long SumSizes(IEnumerable<AnalyzedEvidenceItem> items) =>
        items.Aggregate(0L, (total, item) => SaturatingAdd(total, Math.Max(0, item.SizeBytes)));

    private static bool IsPathWithinRoot(string rootPath, string candidatePath)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));
        return candidate.Equals(root, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static long SaturatingAdd(long left, long right) =>
        left > long.MaxValue - right ? long.MaxValue : left + right;

    private static string UserSafeFileError(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "Access was denied.",
        FileNotFoundException => "The source evidence file is missing.",
        DirectoryNotFoundException => "The source or destination folder is missing.",
        InvalidDataException => exception.Message,
        _ => "An I/O error prevented this file from being exported."
    };

    private static void TryDelete(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}