using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using AndroidRecovery.Core;
using AndroidRecovery.Models;
using AndroidRecovery.Recovery;
using Microsoft.Extensions.Logging;

namespace AndroidRecovery.FileSystem;

public sealed class EvidenceAnalysisService : IAnalysisService
{
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(200);
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly IEvidenceVerifier _evidenceVerifier;
    private readonly ILogger<EvidenceAnalysisService> _logger;

    public EvidenceAnalysisService(IEvidenceVerifier evidenceVerifier, ILogger<EvidenceAnalysisService> logger)
    {
        _evidenceVerifier = evidenceVerifier;
        _logger = logger;
    }

    public async Task<EvidenceAnalysisResult> AnalyzeAsync(
        string packagePath,
        IProgress<AnalysisProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        cancellationToken.ThrowIfCancellationRequested();
        var root = Path.GetFullPath(packagePath);
        progress?.Report(new("Verifying evidence", 0, 0, null));
        _logger.LogInformation("Evidence analysis verification started");
        var verification = await _evidenceVerifier.VerifyAsync(root, cancellationToken).ConfigureAwait(false);
        if (!verification.Succeeded)
        {
            _logger.LogWarning("Evidence analysis stopped because verification failed with {ManifestErrors} manifest errors, {MissingFiles} missing files, and {HashMismatches} hash mismatches",
                verification.ManifestErrors, verification.MissingFiles, verification.HashMismatches);
            return new("", root, false, false, [], verification.Messages);
        }

        var manifestPath = Path.Combine(root, "manifest.json");
        await using var manifestStream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<EvidenceManifest>(manifestStream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Session.SessionId))
        {
            _logger.LogWarning("Evidence analysis stopped because the package manifest has no valid session identifier");
            return new("", root, false, false, [], ["The evidence manifest does not contain a valid session."]);
        }

        _logger.LogInformation("EvidenceAnalysisStarted for session {SessionId}", manifest.Session.SessionId);
        var acquiredFiles = manifest.Files.Where(file => file.Status == AcquiredFileStatus.Acquired).ToArray();
        var items = new List<AnalyzedEvidenceItem>(acquiredFiles.Length);
        var warnings = new List<string>();
        var stopwatch = Stopwatch.StartNew();
        var lastReport = Stopwatch.GetTimestamp();
        progress?.Report(new("Classifying evidence files", 0, acquiredFiles.Length, null));

        foreach (var file in acquiredFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var evidencePath = EvidencePathUtility.ResolveInsideRoot(Path.Combine(root, "files"), file.RelativePath);
                var header = new byte[32 * 1024];
                int bytesRead;
                await using (var stream = new FileStream(evidencePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    header.Length, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    bytesRead = await stream.ReadAsync(header.AsMemory(), cancellationToken).ConfigureAwait(false);
                }

                var classification = EvidenceFileClassifier.Classify(file.FileName, header.AsSpan(0, bytesRead));
                items.Add(new(file.Id, file.SessionId, file.SourcePath, file.RelativePath, file.FileName,
                    file.Extension, file.SizeBytes, file.Sha256, file.AcquiredUtc, classification.Type,
                    classification.Format, classification.Confidence)
                {
                    LocalEvidencePath = evidencePath,
                    ExtensionMismatch = classification.ExtensionMismatch,
                    ConfidenceReason = classification.ConfidenceReason
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                warnings.Add($"Could not analyze evidence file '{file.RelativePath}' ({exception.GetType().Name}).");
                _logger.LogWarning("Evidence file analysis failed for session {SessionId} ({ExceptionType})",
                    manifest.Session.SessionId, exception.GetType().Name);
            }

            var now = Stopwatch.GetTimestamp();
            if (items.Count % 32 == 0 || Stopwatch.GetElapsedTime(lastReport, now) >= ProgressInterval)
            {
                lastReport = now;
                progress?.Report(new("Classifying evidence files", items.Count + warnings.Count,
                    acquiredFiles.Length, file.RelativePath));
            }
        }

        stopwatch.Stop();
        progress?.Report(new("Analysis complete", acquiredFiles.Length, acquiredFiles.Length, null));
        _logger.LogInformation("EvidenceAnalysisCompleted for session {SessionId} with {FilesAnalyzed} files and {WarningCount} warnings in {Duration}",
            manifest.Session.SessionId, items.Count, warnings.Count, stopwatch.Elapsed);
        return new(manifest.Session.SessionId, root, true, true, items, warnings);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}