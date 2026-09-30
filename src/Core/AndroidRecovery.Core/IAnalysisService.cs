using AndroidRecovery.Models;

namespace AndroidRecovery.Core;

public interface IAnalysisService
{
    Task<EvidenceAnalysisResult> AnalyzeAsync(
        string packagePath,
        IProgress<AnalysisProgress>? progress,
        CancellationToken cancellationToken);
}

public interface IPreviewService
{
    Task<EvidencePreviewDetails> CreatePreviewAsync(
        string packagePath,
        AnalyzedEvidenceItem item,
        CancellationToken cancellationToken);
}

public interface IRecoveryService
{
    Task<RecoveryResult> ExportAsync(
        string evidencePackagePath,
        IReadOnlyList<AnalyzedEvidenceItem> items,
        string destinationPath,
        IProgress<RecoveryProgress>? progress,
        CancellationToken cancellationToken);
}