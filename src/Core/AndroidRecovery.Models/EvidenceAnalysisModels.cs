namespace AndroidRecovery.Models;

public enum EvidenceFileType
{
    Image,
    Video,
    Audio,
    Document,
    Archive,
    Database,
    Text,
    Unknown
}

public enum AnalysisConfidence
{
    High,
    Medium,
    Low,
    Unknown
}

public sealed record EvidenceFileClassification(
    EvidenceFileType Type,
    string Format,
    AnalysisConfidence Confidence)
{
    public bool ExtensionMismatch { get; init; }

    public string ConfidenceReason { get; init; } = "";
}

public sealed record AnalysisProgress(
    string Phase,
    long FilesCompleted,
    long FilesTotal,
    string? CurrentRelativePath);

public sealed record AnalyzedEvidenceItem(
    string Id,
    string SessionId,
    string SourcePath,
    string RelativePath,
    string FileName,
    string Extension,
    long SizeBytes,
    string? Sha256,
    DateTimeOffset AcquiredUtc,
    EvidenceFileType Type,
    string DetectedFormat,
    AnalysisConfidence Confidence)
{
    public string LocalEvidencePath { get; init; } = "";

    public bool ExtensionMismatch { get; init; }

    public string ConfidenceReason { get; init; } = "";

    public bool? IsCorrupt { get; init; }

    public bool IsSupported => Type != EvidenceFileType.Unknown;

    public bool PreviewAvailable => Type is EvidenceFileType.Image or EvidenceFileType.Text;

    public string SizeLabel => FormatSize(SizeBytes);

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        var value = (double)bytes;
        var units = new[] { "KB", "MB", "GB", "TB" };
        foreach (var unit in units)
        {
            value /= 1024;
            if (value < 1024 || unit == "TB") return $"{value:0.##} {unit}";
        }

        return $"{value:0.##} TB";
    }
}

public sealed record EvidenceAnalysisResult(
    string SessionId,
    string PackagePath,
    bool Succeeded,
    bool EvidenceVerified,
    IReadOnlyList<AnalyzedEvidenceItem> Items,
    IReadOnlyList<string> Warnings);

public enum EvidencePreviewKind
{
    Image,
    Text,
    Unsupported,
    Unavailable
}

public sealed record EvidencePreviewDetails(
    string FileName,
    EvidencePreviewKind Kind,
    string Format,
    long SizeBytes,
    string SourcePath,
    string LocalEvidencePath,
    string? Sha256,
    AnalysisConfidence Confidence,
    string ConfidenceReason,
    string? TextContent,
    string? Message);

public enum RecoverySessionStatus
{
    Completed,
    CompletedWithWarnings,
    Cancelled,
    Failed
}

public enum RecoveryFileStatus
{
    Verified,
    Failed,
    Cancelled
}

public sealed record RecoveryProgress(
    string SessionId,
    long FilesCompleted,
    long FilesTotal,
    long BytesCompleted,
    long BytesTotal,
    string? CurrentRelativePath,
    TimeSpan ElapsedTime,
    TimeSpan? EstimatedRemainingTime);

public sealed record RecoveryFileResult(
    string EvidenceFileId,
    string SourcePath,
    string DestinationPath,
    long Bytes,
    string? SourceSha256,
    string? DestinationSha256,
    RecoveryFileStatus Status,
    string? Error);

public sealed record RecoveryResult(
    string SessionId,
    DateTimeOffset StartedUtc,
    DateTimeOffset CompletedUtc,
    string EvidencePackagePath,
    string DestinationPath,
    string OutputFolderPath,
    RecoverySessionStatus Status,
    IReadOnlyList<RecoveryFileResult> Files,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    public int SelectedCount { get; init; } = Files.Count;

    public string AcquisitionSessionId { get; init; } = "";

    public string DeviceSerial { get; init; } = "";

    public string DeviceManufacturer { get; init; } = "";

    public string DeviceModel { get; init; } = "";

    public int SuccessfulCount => Files.Count(file => file.Status == RecoveryFileStatus.Verified);

    public int FailedCount => Files.Count(file => file.Status == RecoveryFileStatus.Failed);

    public int CancelledCount => Files.Count(file => file.Status == RecoveryFileStatus.Cancelled);

    public long BytesExported => Files.Where(file => file.Status == RecoveryFileStatus.Verified)
        .Sum(file => file.Bytes);

    public int VerificationPassed => SuccessfulCount;

    public int VerificationFailed => FailedCount;
}