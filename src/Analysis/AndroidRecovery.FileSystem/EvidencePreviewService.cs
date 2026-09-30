using System.Text;
using AndroidRecovery.Core;
using AndroidRecovery.Models;
using AndroidRecovery.Recovery;

namespace AndroidRecovery.FileSystem;

public sealed class EvidencePreviewService : IPreviewService
{
    private const int MaximumTextPreviewBytes = 64 * 1024;
    private readonly IHashService _hashService;

    public EvidencePreviewService(IHashService hashService)
    {
        _hashService = hashService;
    }

    public async Task<EvidencePreviewDetails> CreatePreviewAsync(
        string packagePath,
        AnalyzedEvidenceItem item,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentNullException.ThrowIfNull(item);
        cancellationToken.ThrowIfCancellationRequested();
        var sourcePath = ResolveEvidenceFile(packagePath, item.RelativePath);
        if (!File.Exists(sourcePath))
        {
            return CreateUnavailable(item, "The acquired evidence file is missing.");
        }

        var fileInfo = new FileInfo(sourcePath);
        if (fileInfo.Length != item.SizeBytes)
        {
            return CreateUnavailable(item, "The evidence file size no longer matches the analyzed result.");
        }

        if (string.IsNullOrWhiteSpace(item.Sha256))
        {
            return CreateUnavailable(item, "The evidence file has no recorded SHA-256 and cannot be safely previewed.");
        }

        var actualHash = await _hashService.CalculateSha256Async(sourcePath, cancellationToken).ConfigureAwait(false);
        if (!actualHash.Equals(item.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            return CreateUnavailable(item, "The evidence file SHA-256 no longer matches the verified analysis result.");
        }

        if (item.Type == EvidenceFileType.Image)
        {
            return new(item.FileName, EvidencePreviewKind.Image, item.DetectedFormat, item.SizeBytes,
                item.SourcePath, sourcePath, actualHash, item.Confidence, item.ConfidenceReason, null, null);
        }

        if (item.Type == EvidenceFileType.Text)
        {
            var buffer = new byte[MaximumTextPreviewBytes];
            int bytesRead;
            await using (var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                buffer.Length, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                bytesRead = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            }

            var text = new UTF8Encoding(false, false).GetString(buffer, 0, bytesRead);
            if (fileInfo.Length > bytesRead)
            {
                text += Environment.NewLine + "[Preview truncated]";
            }

            return new(item.FileName, EvidencePreviewKind.Text, item.DetectedFormat, item.SizeBytes,
                item.SourcePath, sourcePath, actualHash, item.Confidence, item.ConfidenceReason, text, null);
        }

        return new(item.FileName, EvidencePreviewKind.Unsupported, item.DetectedFormat, item.SizeBytes,
            item.SourcePath, sourcePath, actualHash, item.Confidence, item.ConfidenceReason, null,
            "Preview is unavailable for this file type. The original acquired file can still be exported.");
    }

    internal static string ResolveEvidenceFile(string packagePath, string relativePath)
    {
        var filesRoot = Path.GetFullPath(Path.Combine(packagePath, "files"));
        var normalized = EvidencePathUtility.NormalizeRelativePath(relativePath);
        var sourcePath = EvidencePathUtility.ResolveInsideRoot(filesRoot, normalized);
        EnsureNoReparsePoints(filesRoot, normalized);
        return sourcePath;
    }

    internal static void EnsureNoReparsePoints(string rootPath, string relativePath)
    {
        var currentPath = Path.GetFullPath(rootPath);
        if ((File.GetAttributes(currentPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new ArgumentException("Evidence paths cannot traverse reparse points.", nameof(relativePath));
        }

        foreach (var segment in relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            currentPath = Path.Combine(currentPath, segment);
            if (!File.Exists(currentPath) && !Directory.Exists(currentPath))
            {
                break;
            }

            if ((File.GetAttributes(currentPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new ArgumentException("Evidence paths cannot traverse reparse points.", nameof(relativePath));
            }
        }
    }

    private static EvidencePreviewDetails CreateUnavailable(AnalyzedEvidenceItem item, string message) =>
        new(item.FileName, EvidencePreviewKind.Unavailable, item.DetectedFormat, item.SizeBytes,
            item.SourcePath, item.LocalEvidencePath, item.Sha256, item.Confidence, item.ConfidenceReason, null, message);
}