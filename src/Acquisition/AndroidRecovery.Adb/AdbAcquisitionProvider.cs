using System.Diagnostics;
using AndroidRecovery.Models;
using AndroidRecovery.Recovery;
using Microsoft.Extensions.Logging;

namespace AndroidRecovery.Adb;

public sealed class AdbAcquisitionProvider : IAcquisitionProvider
{
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(150);
    private const long MinimumFreeSpaceReserveBytes = 512L * 1024 * 1024;
    private readonly IAdbService _adbService;
    private readonly IDeviceFileEnumerator _fileEnumerator;
    private readonly IEvidencePackageWriterFactory _packageWriterFactory;
    private readonly IEvidenceVerifier _evidenceVerifier;
    private readonly IHashService _hashService;
    private readonly ILogger<AdbAcquisitionProvider> _logger;
    private readonly long _minimumFreeSpaceReserveBytes;

    public AdbAcquisitionProvider(
        IAdbService adbService,
        IDeviceFileEnumerator fileEnumerator,
        IEvidencePackageWriterFactory packageWriterFactory,
        IEvidenceVerifier evidenceVerifier,
        IHashService hashService,
        ILogger<AdbAcquisitionProvider> logger,
        long minimumFreeSpaceReserveBytes = MinimumFreeSpaceReserveBytes)
    {
        if (minimumFreeSpaceReserveBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumFreeSpaceReserveBytes));
        }

        _adbService = adbService;
        _fileEnumerator = fileEnumerator;
        _packageWriterFactory = packageWriterFactory;
        _evidenceVerifier = evidenceVerifier;
        _hashService = hashService;
        _logger = logger;
        _minimumFreeSpaceReserveBytes = minimumFreeSpaceReserveBytes;
    }

    public string Name => "ADB";

    public async Task<AcquisitionResult> AcquireAsync(
        AndroidDevice device,
        AcquisitionOptions options,
        IProgress<AcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var stopwatch = Stopwatch.StartNew();
        var startedUtc = DateTimeOffset.UtcNow;
        var sessionId = $"AR-{startedUtc:yyyyMMdd}-{Guid.NewGuid():N}";
        var errors = new List<AcquisitionError>();
        var warnings = new List<AcquisitionError>();
        var session = CreateSession(sessionId, device, options, startedUtc, AcquisitionStatus.Pending);

        if (cancellationToken.IsCancellationRequested)
        {
            session = CompleteSession(session, AcquisitionStatus.Cancelled, DateTimeOffset.UtcNow,
                0, 0, 0, 0, errors, warnings, null);
            Report(progress, session, stopwatch, AcquisitionStatus.Cancelled, null);
            return new AcquisitionResult
            {
                Success = false,
                Status = AcquisitionStatus.Cancelled,
                Session = session,
                Errors = errors,
                Warnings = warnings,
                Duration = stopwatch.Elapsed
            };
        }

        var validationError = Validate(device, options);
        if (validationError is not null)
        {
            errors.Add(validationError);
            session = CompleteSession(session, AcquisitionStatus.Failed, startedUtc, 0, 0, 0, 0, errors, warnings, null);
            Report(progress, session, stopwatch, AcquisitionStatus.Failed, null);
            _logger.LogWarning("Acquisition validation failed for session {SessionId} with code {ErrorCode}",
                sessionId, validationError.ErrorCode);
            return new AcquisitionResult
            {
                Success = false,
                Status = AcquisitionStatus.Failed,
                Session = session,
                Errors = errors,
                Warnings = warnings,
                Duration = stopwatch.Elapsed
            };
        }

        if (!_adbService.IsAvailable)
        {
            errors.Add(CreateError("ValidateAdb", "AdbUnavailable",
                "The configured ADB executable is unavailable.", AcquisitionErrorSeverity.FatalError, false));
            session = CompleteSession(session, AcquisitionStatus.Failed, startedUtc, 0, 0, 0, 0, errors, warnings, null);
            Report(progress, session, stopwatch, AcquisitionStatus.Failed, null);
            return new AcquisitionResult
            {
                Success = false,
                Status = AcquisitionStatus.Failed,
                Session = session,
                Errors = errors,
                Warnings = warnings,
                Duration = stopwatch.Elapsed
            };
        }

        AcquisitionError? authorizationError;
        try
        {
            authorizationError = await ValidateLiveAuthorizationAsync(device, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            session = CompleteSession(session, AcquisitionStatus.Cancelled, DateTimeOffset.UtcNow,
                0, 0, 0, 0, errors, warnings, null);
            Report(progress, session, stopwatch, AcquisitionStatus.Cancelled, null);
            return new AcquisitionResult
            {
                Success = false,
                Status = AcquisitionStatus.Cancelled,
                Session = session,
                Errors = errors,
                Warnings = warnings,
                Duration = stopwatch.Elapsed
            };
        }
        if (authorizationError is not null)
        {
            errors.Add(authorizationError);
            session = CompleteSession(session, AcquisitionStatus.Failed, startedUtc, 0, 0, 0, 0, errors, warnings, null);
            Report(progress, session, stopwatch, AcquisitionStatus.Failed, null);
            _logger.LogWarning("Acquisition authorization validation failed for session {SessionId} with code {ErrorCode}",
                sessionId, authorizationError.ErrorCode);
            return new AcquisitionResult
            {
                Success = false,
                Status = AcquisitionStatus.Failed,
                Session = session,
                Errors = errors,
                Warnings = warnings,
                Duration = stopwatch.Elapsed
            };
        }

        string? packagePath = null;
        IEvidencePackageWriter? writer = null;
        var filesDiscovered = 0L;
        var filesAcquired = 0L;
        var bytesDiscovered = 0L;
        var bytesAcquired = 0L;
        var fileRecords = new List<EvidenceFileRecord>();
        var collisionPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var disconnected = false;
        var cancelled = false;
        var lastProgress = Stopwatch.GetTimestamp();

        _logger.LogInformation("AcquisitionStarted for session {SessionId} using {Provider}", sessionId, Name);
        Report(progress, session, stopwatch, AcquisitionStatus.Preparing, null);

        try
        {
            var packageSession = session with { Status = AcquisitionStatus.Preparing };
            writer = await _packageWriterFactory.CreateAsync(packageSession, options, cancellationToken).ConfigureAwait(false);
            packagePath = writer.Package.RootPath;
            _logger.LogInformation("EvidencePackageCreated for session {SessionId}", sessionId);
            _logger.LogInformation("EnumerationStarted for session {SessionId}", sessionId);

            var enumeration = await _fileEnumerator.EnumerateAsync(device, options.SelectedCategories,
                progress, sessionId, cancellationToken).ConfigureAwait(false);
            filesDiscovered = enumeration.Files.Count;
            bytesDiscovered = enumeration.Files.Where(file => file.SizeBytes is >= 0)
                .Aggregate(0L, (total, file) => SaturatingAdd(total, file.SizeBytes!.Value));
            var byteTotalReliable = enumeration.Files.All(file => file.SizeBytes is >= 0);
            errors.AddRange(enumeration.Errors);
            warnings.AddRange(enumeration.Warnings);
            foreach (var error in enumeration.Errors.Concat(enumeration.Warnings))
            {
                await writer.RecordErrorAsync(error, CancellationToken.None).ConfigureAwait(false);
            }

            disconnected = enumeration.DeviceDisconnected;
            _logger.LogInformation("EnumerationCompleted for session {SessionId} with {FileCount} files and {ByteCount} bytes",
                sessionId, filesDiscovered, bytesDiscovered);
            Report(progress, session, stopwatch, AcquisitionStatus.Running, null,
                filesDiscovered, 0, bytesDiscovered, 0, errors.Count, warnings.Count,
                byteTotalReliable: byteTotalReliable);

            var destinationSpaceError = ValidateDestinationSpace(options.DestinationPath, filesDiscovered,
                bytesDiscovered, byteTotalReliable, _minimumFreeSpaceReserveBytes);
            if (destinationSpaceError is not null)
            {
                errors.Add(destinationSpaceError);
                await writer.RecordErrorAsync(destinationSpaceError, CancellationToken.None).ConfigureAwait(false);
                _logger.LogWarning("Acquisition stopped before transfer for session {SessionId} due to insufficient destination space",
                    sessionId);
            }

            foreach (var deviceFile in enumeration.Files)
            {
                if (destinationSpaceError is not null)
                {
                    break;
                }

                cancellationToken.ThrowIfCancellationRequested();
                var category = GetCategory(deviceFile.SourcePath, options.SelectedCategories);
                var requestedRelativePath = options.PreserveFolderStructure
                    ? deviceFile.RelativePath
                    : Path.Combine(category.ToString(), Path.GetFileName(deviceFile.RelativePath));
                var finalRelativePath = ReserveCollisionSafePath(requestedRelativePath, collisionPaths,
                    writer.Package.FilesPath);
                var destination = EvidencePathUtility.ResolveInsideRoot(writer.Package.FilesPath, finalRelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var temporaryPath = destination + ".partial-" + Guid.NewGuid().ToString("N");
                var currentPath = deviceFile.SourcePath;
                Report(progress, session, stopwatch, AcquisitionStatus.Running, currentPath,
                    filesDiscovered, filesAcquired, bytesDiscovered, bytesAcquired, errors.Count, warnings.Count,
                    destination, byteTotalReliable);
                _logger.LogInformation("FileAcquisitionStarted for session {SessionId} at {SourcePath}",
                    sessionId, deviceFile.SourcePath);

                AdbCommandResult pullResult;
                try
                {
                    pullResult = await _adbService.PullFileAsync(device.Id, deviceFile.SourcePath,
                        temporaryPath, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    TryDeleteTemporary(temporaryPath);
                    throw;
                }

                if (!pullResult.Succeeded)
                {
                    TryDeleteTemporary(temporaryPath);
                    var isDisconnect = IsDisconnected(pullResult);
                    var fileError = CreateError("PullFile", isDisconnect ? "DeviceDisconnected" : pullResult.ErrorCode ?? "PullFailed",
                        isDisconnect ? "The Android device disconnected during file acquisition."
                            : "This file could not be copied from the Android device.",
                        isDisconnect ? AcquisitionErrorSeverity.FatalError : AcquisitionErrorSeverity.RecoverableError,
                        !isDisconnect, deviceFile.SourcePath);
                    errors.Add(fileError);
                    await writer.RecordErrorAsync(fileError, CancellationToken.None).ConfigureAwait(false);
                    var failedRecord = CreateFileRecord(deviceFile, category, finalRelativePath, null,
                        AcquiredFileStatus.Failed, fileError.Message, sessionId);
                    await writer.RecordFileAsync(failedRecord, CancellationToken.None).ConfigureAwait(false);
                    fileRecords.Add(failedRecord);
                    _logger.LogWarning("FileAcquisitionFailed for session {SessionId} with code {ErrorCode}",
                        sessionId, fileError.ErrorCode);
                    if (isDisconnect)
                    {
                        disconnected = true;
                        break;
                    }

                    continue;
                }

                File.Move(temporaryPath, destination, overwrite: false);
                var actualSize = new FileInfo(destination).Length;
                if (deviceFile.SizeBytes is long expectedSize && expectedSize != actualSize)
                {
                    var sizeError = CreateError("ValidateCopiedFile", "FileSizeMismatch",
                        "The copied file size differs from the size reported by Android.",
                        AcquisitionErrorSeverity.RecoverableError, true, deviceFile.SourcePath);
                    errors.Add(sizeError);
                    await writer.RecordErrorAsync(sizeError, CancellationToken.None).ConfigureAwait(false);
                    var mismatchedRecord = CreateFileRecord(deviceFile, category, finalRelativePath, null,
                        AcquiredFileStatus.Failed, sizeError.Message, sessionId, actualSize);
                    await writer.RecordFileAsync(mismatchedRecord, CancellationToken.None).ConfigureAwait(false);
                    fileRecords.Add(mismatchedRecord);
                    continue;
                }

                string? sha256 = null;
                if (options.CalculateHashes)
                {
                    try
                    {
                        sha256 = await _hashService.CalculateSha256Async(destination, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        var cancelledHash = CreateError("CalculateSha256", "HashCancelled",
                            "Hashing was cancelled after the file copy; the preserved copy is recorded as unverified.",
                            AcquisitionErrorSeverity.Warning, true, deviceFile.SourcePath);
                        warnings.Add(cancelledHash);
                        await writer.RecordErrorAsync(cancelledHash, CancellationToken.None).ConfigureAwait(false);
                        var unverifiedRecord = CreateFileRecord(deviceFile, category, finalRelativePath, null,
                            AcquiredFileStatus.Failed, cancelledHash.Message, sessionId, actualSize);
                        await writer.RecordFileAsync(unverifiedRecord, CancellationToken.None).ConfigureAwait(false);
                        fileRecords.Add(unverifiedRecord);
                        throw;
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        var hashError = CreateError("CalculateSha256", "HashFailed",
                            "The acquired file could not be hashed; it is not marked as acquired.",
                            AcquisitionErrorSeverity.RecoverableError, true, deviceFile.SourcePath, exception.GetType().Name);
                        errors.Add(hashError);
                        await writer.RecordErrorAsync(hashError, CancellationToken.None).ConfigureAwait(false);
                        var hashFailedRecord = CreateFileRecord(deviceFile, category, finalRelativePath, null,
                            AcquiredFileStatus.Failed, hashError.Message, sessionId, actualSize);
                        await writer.RecordFileAsync(hashFailedRecord, CancellationToken.None).ConfigureAwait(false);
                        fileRecords.Add(hashFailedRecord);
                        continue;
                    }
                }

                var acquiredRecord = CreateFileRecord(deviceFile, category, finalRelativePath, sha256,
                    AcquiredFileStatus.Acquired, null, sessionId, actualSize);
                await writer.RecordFileAsync(acquiredRecord, CancellationToken.None).ConfigureAwait(false);
                fileRecords.Add(acquiredRecord);
                filesAcquired++;
                bytesAcquired = SaturatingAdd(bytesAcquired, actualSize);
                _logger.LogInformation("FileAcquisitionCompleted for session {SessionId} with {FileSize} bytes",
                    sessionId, actualSize);

                var now = Stopwatch.GetTimestamp();
                if (filesAcquired % 16 == 0 || Stopwatch.GetElapsedTime(lastProgress, now) >= ProgressInterval)
                {
                    lastProgress = now;
                    Report(progress, session, stopwatch, AcquisitionStatus.Running, currentPath,
                        filesDiscovered, filesAcquired, bytesDiscovered, bytesAcquired, errors.Count, warnings.Count,
                        destination, byteTotalReliable);
                }
            }

            if (disconnected && errors.All(error => error.ErrorCode != "DeviceDisconnected"))
            {
                var disconnectError = CreateError("Acquisition", "DeviceDisconnected",
                    "The Android device disconnected during acquisition.", AcquisitionErrorSeverity.FatalError, false);
                errors.Add(disconnectError);
                await writer.RecordErrorAsync(disconnectError, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
            Report(progress, session, stopwatch, AcquisitionStatus.Cancelling, null,
                filesDiscovered, filesAcquired, bytesDiscovered, bytesAcquired, errors.Count, warnings.Count,
                packagePath);
            var cancellationError = CreateError("Acquisition", "Cancelled",
                "The acquisition was cancelled by the user. Already acquired evidence has been preserved.",
                AcquisitionErrorSeverity.Warning, true);
            warnings.Add(cancellationError);
            if (writer is not null)
            {
                await writer.RecordErrorAsync(cancellationError, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var failure = CreateError("Acquisition", "AcquisitionFailed",
                "Acquisition failed. Any files already copied have been preserved.",
                AcquisitionErrorSeverity.FatalError, false, exceptionType: exception.GetType().Name);
            errors.Add(failure);
            if (writer is not null)
            {
                try
                {
                    await writer.RecordErrorAsync(failure, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception recordException)
                {
                    _logger.LogError("Could not record acquisition failure for session {SessionId} ({ExceptionType})",
                        sessionId, recordException.GetType().Name);
                }
            }

            _logger.LogError("AcquisitionFailed for session {SessionId} ({ExceptionType})",
                sessionId, exception.GetType().Name);
        }

        var endTimeUtc = DateTimeOffset.UtcNow;
        var fatal = disconnected || errors.Any(error => error.Severity == AcquisitionErrorSeverity.FatalError);
        var status = cancelled ? AcquisitionStatus.Cancelled
            : fatal ? AcquisitionStatus.Failed
            : errors.Count > 0 || warnings.Count > 0 ? AcquisitionStatus.CompletedWithWarnings
            : AcquisitionStatus.Completed;
        session = CompleteSession(session, status, endTimeUtc, filesDiscovered, filesAcquired,
            bytesDiscovered, bytesAcquired, errors, warnings, packagePath);

        EvidenceVerificationResult? verification = null;
        if (writer is not null)
        {
            try
            {
                await writer.FinalizeAsync(session, options.SelectedCategories.ToArray(), errors.Concat(warnings).ToArray(),
                    CancellationToken.None).ConfigureAwait(false);
                if (options.VerifyAfterAcquisition)
                {
                    _logger.LogInformation("EvidenceVerificationStarted for session {SessionId}", sessionId);
                    verification = await _evidenceVerifier.VerifyAsync(writer.Package.RootPath, CancellationToken.None)
                        .ConfigureAwait(false);
                    await writer.RecordVerificationAsync(verification, CancellationToken.None).ConfigureAwait(false);
                    if (!verification.Succeeded && status is AcquisitionStatus.Completed or AcquisitionStatus.CompletedWithWarnings)
                    {
                        status = AcquisitionStatus.CompletedWithWarnings;
                        var verifyError = CreateError("VerifyEvidence", "VerificationFailed",
                            "Acquisition completed, but evidence package verification did not pass.",
                            AcquisitionErrorSeverity.RecoverableError, true);
                        warnings.Add(verifyError);
                        await writer.RecordErrorAsync(verifyError, CancellationToken.None).ConfigureAwait(false);
                        session = CompleteSession(session, status, endTimeUtc, filesDiscovered, filesAcquired,
                            bytesDiscovered, bytesAcquired, errors, warnings, packagePath);
                        await writer.FinalizeAsync(session, options.SelectedCategories.ToArray(), errors.Concat(warnings).ToArray(),
                            CancellationToken.None).ConfigureAwait(false);
                        await writer.RecordVerificationAsync(verification, CancellationToken.None).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var finalizeError = CreateError("FinalizeEvidence", "EvidenceFinalizeFailed",
                    "The evidence package could not be fully finalized. Already copied files have been preserved.",
                    AcquisitionErrorSeverity.FatalError, false, exceptionType: exception.GetType().Name);
                errors.Add(finalizeError);
                status = AcquisitionStatus.Failed;
                session = CompleteSession(session, status, endTimeUtc, filesDiscovered, filesAcquired,
                    bytesDiscovered, bytesAcquired, errors, warnings, packagePath);
                _logger.LogError("Evidence finalization failed for session {SessionId} ({ExceptionType})",
                    sessionId, exception.GetType().Name);
            }

            await writer.DisposeAsync().ConfigureAwait(false);
        }

        stopwatch.Stop();
        Report(progress, session, stopwatch, status, null, filesDiscovered, filesAcquired,
            bytesDiscovered, bytesAcquired, errors.Count, warnings.Count, packagePath);
        _logger.LogInformation("AcquisitionCompleted for session {SessionId} with status {Status}, {FilesAcquired} files and {BytesAcquired} bytes",
            sessionId, status, filesAcquired, bytesAcquired);

        return new AcquisitionResult
        {
            Success = status is AcquisitionStatus.Completed or AcquisitionStatus.CompletedWithWarnings,
            Status = status,
            Session = session,
            EvidencePackage = writer?.Package,
            Verification = verification,
            Errors = errors,
            Warnings = warnings,
            Duration = stopwatch.Elapsed
        };
    }

    private async Task<AcquisitionError?> ValidateLiveAuthorizationAsync(AndroidDevice device, CancellationToken cancellationToken)
    {
        var result = await _adbService.ExecuteAsync(["devices", "-l"], cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return CreateError("ValidateDevice", result.ErrorCode ?? "DeviceCheckFailed",
                "ADB could not confirm that the selected device is still connected.", AcquisitionErrorSeverity.FatalError, false);
        }

        var entry = AdbDeviceListParser.Parse(result.StandardOutput).Devices
            .FirstOrDefault(candidate => string.Equals(candidate.Id, device.Id, StringComparison.Ordinal));
        if (entry is null)
        {
            return CreateError("ValidateDevice", "DeviceDisconnected",
                "The selected Android device is no longer connected.", AcquisitionErrorSeverity.FatalError, false);
        }

        return entry.State switch
        {
            AdbDeviceState.Device => null,
            AdbDeviceState.Unauthorized => CreateError("ValidateDevice", "DeviceUnauthorized",
                "Unlock your Android device and accept the USB debugging authorization prompt.",
                AcquisitionErrorSeverity.FatalError, false),
            AdbDeviceState.Offline => CreateError("ValidateDevice", "DeviceOffline",
                "The Android device is offline. Check the USB connection and refresh device status.",
                AcquisitionErrorSeverity.FatalError, false),
            _ => CreateError("ValidateDevice", "DeviceStateUnknown",
                "The current ADB device state is unknown; acquisition was stopped safely.",
                AcquisitionErrorSeverity.FatalError, false)
        };
    }

    private static AcquisitionError? Validate(AndroidDevice? device, AcquisitionOptions options)
    {
        if (device is null)
        {
            return CreateError("ValidateOptions", "NoDeviceSelected", "Select an Android device before starting acquisition.",
                AcquisitionErrorSeverity.FatalError, false);
        }

        if (!device.Authorized)
        {
            return CreateError("ValidateDevice", device.AdbState switch
            {
                AdbDeviceState.Unauthorized => "DeviceUnauthorized",
                AdbDeviceState.Offline => "DeviceOffline",
                _ => "DeviceNotAuthorized"
            }, device.AdbState == AdbDeviceState.Unauthorized
                ? "Unlock your Android device and accept the USB debugging authorization prompt."
                : "The selected Android device is not authorized for ADB acquisition.",
                AcquisitionErrorSeverity.FatalError, false);
        }

        if (options.SelectedCategories.Count == 0)
        {
            return CreateError("ValidateOptions", "NoCategoriesSelected", "Select at least one accessible data category.",
                AcquisitionErrorSeverity.FatalError, false);
        }

        if (!string.Equals(options.AcquisitionMethod, "ADB", StringComparison.OrdinalIgnoreCase))
        {
            return CreateError("ValidateOptions", "UnsupportedAcquisitionMethod", "Only read-only ADB acquisition is supported.",
                AcquisitionErrorSeverity.FatalError, false);
        }

        if (string.IsNullOrWhiteSpace(options.DestinationPath))
        {
            return CreateError("ValidateDestination", "DestinationRequired", "Choose a destination directory.",
                AcquisitionErrorSeverity.FatalError, false);
        }

        return null;
    }

    private static AcquisitionSession CreateSession(
        string sessionId,
        AndroidDevice device,
        AcquisitionOptions options,
        DateTimeOffset startedUtc,
        AcquisitionStatus status) => new()
        {
            SessionId = sessionId,
            DeviceId = device?.Id ?? "",
            DeviceSerial = device?.Id ?? "",
            DeviceManufacturer = device?.Manufacturer ?? "",
            DeviceModel = device?.Model ?? "",
            AndroidVersion = device?.AndroidVersion ?? "",
            ApiLevel = device?.ApiLevel,
            AcquisitionMethod = options.AcquisitionMethod,
            Categories = options.SelectedCategories.ToArray(),
            StartTimeUtc = startedUtc,
            Status = status,
            DestinationPath = options.DestinationPath
        };

    private static AcquisitionSession CompleteSession(
        AcquisitionSession session,
        AcquisitionStatus status,
        DateTimeOffset endTimeUtc,
        long discovered,
        long acquired,
        long bytesDiscovered,
        long bytesAcquired,
        IReadOnlyCollection<AcquisitionError> errors,
        IReadOnlyCollection<AcquisitionError> warnings,
        string? packagePath) => session with
        {
            EndTimeUtc = endTimeUtc,
            Status = status,
            TotalFilesDiscovered = discovered,
            TotalFilesAcquired = acquired,
            TotalBytesDiscovered = bytesDiscovered,
            TotalBytesAcquired = bytesAcquired,
            ErrorCount = errors.Count,
            WarningCount = warnings.Count,
            EvidencePackagePath = packagePath
        };

    private static EvidenceFileRecord CreateFileRecord(
        DeviceFile deviceFile,
        AcquisitionCategory category,
        string relativePath,
        string? sha256,
        AcquiredFileStatus status,
        string? errorMessage,
        string sessionId,
        long? actualSize = null) => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            SessionId = sessionId,
            Category = category,
            SourcePath = deviceFile.SourcePath,
            RelativePath = relativePath,
            FileName = Path.GetFileName(relativePath),
            Extension = Path.GetExtension(relativePath),
            SizeBytes = actualSize ?? deviceFile.SizeBytes ?? 0,
            Sha256 = sha256,
            AcquiredUtc = DateTimeOffset.UtcNow,
            Status = status,
            ErrorMessage = errorMessage
        };

    private static AcquisitionCategory GetCategory(string sourcePath, IReadOnlySet<AcquisitionCategory> categories)
    {
        var segments = sourcePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var directory = segments.ElementAtOrDefault(1);
        var extension = Path.GetExtension(sourcePath);
        foreach (var category in categories)
        {
            if (AcquisitionCategorySourceMap.GetSources(category).Any(source =>
                    string.Equals(source.DirectoryName, directory, StringComparison.OrdinalIgnoreCase)
                    && (source.Extensions is null || source.Extensions.Contains(extension))))
            {
                return category;
            }
        }

        return categories.FirstOrDefault();
    }

    private static string ReserveCollisionSafePath(string relativePath, HashSet<string> reserved, string filesRoot)
    {
        var normalized = EvidencePathUtility.NormalizeRelativePath(relativePath);
        var directory = Path.GetDirectoryName(normalized) ?? "";
        var fileName = Path.GetFileNameWithoutExtension(normalized);
        var extension = Path.GetExtension(normalized);
        for (var index = 0; ; index++)
        {
            var candidateName = index == 0 ? fileName : $"{fileName} ({index})";
            var candidate = string.IsNullOrEmpty(directory)
                ? candidateName + extension
                : Path.Combine(directory, candidateName + extension);
            var destination = EvidencePathUtility.ResolveInsideRoot(filesRoot, candidate);
            if (reserved.Add(candidate) && !File.Exists(destination))
            {
                return candidate;
            }
        }
    }

    private static void Report(
        IProgress<AcquisitionProgress>? progress,
        AcquisitionSession session,
        Stopwatch stopwatch,
        AcquisitionStatus status,
        string? currentFile,
        long filesDiscovered = 0,
        long filesAcquired = 0,
        long bytesDiscovered = 0,
        long bytesAcquired = 0,
        int errorCount = 0,
        int warningCount = 0,
        string? currentDestinationPath = null,
        bool byteTotalReliable = true) => progress?.Report(new AcquisitionProgress
        {
            SessionId = session.SessionId,
            Status = status,
            FilesDiscovered = filesDiscovered,
            FilesAcquired = filesAcquired,
            BytesDiscovered = bytesDiscovered,
            BytesAcquired = bytesAcquired,
            IsByteTotalReliable = byteTotalReliable,
            CurrentFile = currentFile is null ? null : Path.GetFileName(currentFile),
            CurrentSourcePath = currentFile,
            CurrentDestinationPath = currentDestinationPath,
            ElapsedTime = stopwatch.Elapsed,
            ErrorCount = errorCount,
            WarningCount = warningCount,
            EstimatedRemainingTime = EstimateRemainingTime(stopwatch.Elapsed,
                bytesDiscovered, bytesAcquired, filesDiscovered, filesAcquired, status, byteTotalReliable)
        });

    private static TimeSpan? EstimateRemainingTime(
        TimeSpan elapsed,
        long bytesDiscovered,
        long bytesAcquired,
        long filesDiscovered,
        long filesAcquired,
        AcquisitionStatus status,
        bool byteTotalReliable)
    {
        if (status != AcquisitionStatus.Running || elapsed <= TimeSpan.Zero)
        {
            return null;
        }

        var useBytes = byteTotalReliable && bytesDiscovered > 0;
        var total = useBytes ? bytesDiscovered : filesDiscovered;
        var completed = useBytes ? bytesAcquired : filesAcquired;
        if (total <= completed || completed <= 0)
        {
            return null;
        }

        var secondsRemaining = elapsed.TotalSeconds * (total - completed) / completed;
        return double.IsFinite(secondsRemaining) && secondsRemaining >= 0
            ? TimeSpan.FromSeconds(Math.Min(secondsRemaining, TimeSpan.MaxValue.TotalSeconds))
            : null;
    }

    private static bool IsDisconnected(AdbCommandResult result)
    {
        var error = result.StandardError;
        return error.Contains("device not found", StringComparison.OrdinalIgnoreCase)
            || error.Contains("device offline", StringComparison.OrdinalIgnoreCase)
            || error.Contains("no devices/emulators found", StringComparison.OrdinalIgnoreCase)
            || error.Contains("transport error", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDeleteTemporary(string path)
    {
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

    private static AcquisitionError? ValidateDestinationSpace(
        string destinationPath,
        long fileCount,
        long bytesRequired,
        bool byteTotalReliable,
        long minimumFreeSpaceReserveBytes)
    {
        if (fileCount == 0)
        {
            return null;
        }

        long availableBytes;
        try
        {
            var destinationRoot = Path.GetPathRoot(Path.GetFullPath(destinationPath));
            if (string.IsNullOrWhiteSpace(destinationRoot))
            {
                return null;
            }

            var drive = new DriveInfo(destinationRoot);
            if (!drive.IsReady)
            {
                return null;
            }

            availableBytes = drive.AvailableFreeSpace;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (byteTotalReliable && SaturatingAdd(bytesRequired, minimumFreeSpaceReserveBytes) <= availableBytes)
        {
            return null;
        }

        if (!byteTotalReliable && availableBytes >= minimumFreeSpaceReserveBytes)
        {
            return null;
        }

        var message = byteTotalReliable
            ? $"The destination has {availableBytes:N0} bytes free, but the discovered files require {bytesRequired:N0} bytes plus a {minimumFreeSpaceReserveBytes:N0}-byte safety reserve. Choose a larger destination."
            : $"The destination has less than the required {minimumFreeSpaceReserveBytes:N0}-byte free-space reserve. Choose a larger destination.";
        return CreateError("ValidateDestinationSpace", "InsufficientDestinationSpace", message,
            AcquisitionErrorSeverity.FatalError, true);
    }

    private static long SaturatingAdd(long current, long addition) =>
        current > long.MaxValue - addition ? long.MaxValue : current + addition;

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
}