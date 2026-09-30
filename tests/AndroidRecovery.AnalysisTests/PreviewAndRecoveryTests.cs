using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using AndroidRecovery.FileSystem;
using AndroidRecovery.Models;
using AndroidRecovery.Recovery;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AndroidRecovery.AnalysisTests;

public sealed class PreviewAndRecoveryTests
{
    [Fact]
    public async Task ExportAsync_CopiesAndVerifiesSelectedEvidenceFileAndWritesReport()
    {
        using var workspace = new TemporaryDirectory();
        var packagePath = CreateEvidenceFile(workspace.Path, "Pictures/photo.png", [1, 2, 3, 4], out var item);
        var destination = Path.Combine(workspace.Path, "export");
        var service = CreateRecoveryService();

        var result = await service.ExportAsync(packagePath, [item], destination, null, CancellationToken.None);

        var exported = Assert.Single(result.Files);
        Assert.Equal(RecoverySessionStatus.Completed, result.Status);
        Assert.Equal(RecoveryFileStatus.Verified, exported.Status);
        Assert.Equal("session-one", result.AcquisitionSessionId);
        Assert.Equal("TestMaker", result.DeviceManufacturer);
        Assert.Equal(item.Sha256, exported.DestinationSha256);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(exported.DestinationPath));
        var reportPath = Path.Combine(result.OutputFolderPath, "recovery-report.json");
        Assert.True(File.Exists(reportPath));
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));
        Assert.Equal("AndroidRecovery", report.RootElement.GetProperty("application").GetProperty("name").GetString());
        var recovery = report.RootElement.GetProperty("recovery");
        Assert.Equal("session-one", recovery.GetProperty("acquisitionSessionId").GetString());
        var reportFile = recovery.GetProperty("files")[0];
        Assert.Equal(item.Sha256, reportFile.GetProperty("sourceSha256").GetString());
        Assert.Equal(item.Sha256, reportFile.GetProperty("destinationSha256").GetString());
    }

    [Fact]
    public async Task ExportAsync_KeepsBothFilesWhenRelativeDestinationCollides()
    {
        using var workspace = new TemporaryDirectory();
        var packagePath = CreateEvidenceFile(workspace.Path, "Pictures/photo.jpg", [1, 2, 3], out var first);
        var second = first with { Id = "file-two", SourcePath = "/sdcard/Other/photo.jpg" };
        var result = await CreateRecoveryService().ExportAsync(packagePath, [first, second],
            Path.Combine(workspace.Path, "export"), null, CancellationToken.None);

        Assert.Equal(RecoverySessionStatus.Completed, result.Status);
        Assert.Equal(2, result.SuccessfulCount);
        Assert.Equal(2, result.Files.Select(file => file.DestinationPath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("photo (1).jpg", result.Files[1].DestinationPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExportAsync_RejectsPathTraversalWithoutWritingOutsideOutputFolder()
    {
        using var workspace = new TemporaryDirectory();
        var packagePath = CreateEvidenceFile(workspace.Path, "Pictures/photo.jpg", [1], out var item);
        var malicious = item with { RelativePath = "..\\escape.txt" };
        var result = await CreateRecoveryService().ExportAsync(packagePath, [malicious],
            Path.Combine(workspace.Path, "export"), null, CancellationToken.None);

        Assert.Equal(RecoverySessionStatus.CompletedWithWarnings, result.Status);
        Assert.Equal(RecoveryFileStatus.Failed, Assert.Single(result.Files).Status);
        Assert.False(File.Exists(Path.Combine(workspace.Path, "escape.txt")));
    }

    [Fact]
    public async Task ExportAsync_RejectsKnownInsufficientDestinationSpace()
    {
        using var workspace = new TemporaryDirectory();
        var packagePath = CreateEvidenceFile(workspace.Path, "Pictures/photo.jpg", [1], out var item);
        var oversized = item with { SizeBytes = long.MaxValue };
        var result = await CreateRecoveryService().ExportAsync(packagePath, [oversized],
            Path.Combine(workspace.Path, "export"), null, CancellationToken.None);

        Assert.Equal(RecoverySessionStatus.Failed, result.Status);
        Assert.Contains(result.Errors, error => error.Contains("Not enough destination space", StringComparison.Ordinal));
        Assert.Empty(result.Files);
    }

    [Fact]
    public async Task ExportAsync_RejectsDestinationInsideEvidencePackage()
    {
        using var workspace = new TemporaryDirectory();
        var packagePath = CreateEvidenceFile(workspace.Path, "Pictures/photo.jpg", [1, 2], out var item);
        var nestedDestination = Path.Combine(packagePath, "recovered");

        var result = await CreateRecoveryService().ExportAsync(packagePath, [item], nestedDestination,
            null, CancellationToken.None);

        Assert.Equal(RecoverySessionStatus.Failed, result.Status);
        Assert.Contains(result.Errors, error => error.Contains("outside the source evidence package", StringComparison.Ordinal));
        Assert.False(Directory.Exists(nestedDestination));
    }

    [Fact]
    public async Task PreviewAsync_VerifiesImageHashBeforeReturningPreviewPath()
    {
        using var workspace = new TemporaryDirectory();
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var packagePath = CreateEvidenceFile(workspace.Path, "Pictures/photo.png", imageBytes, out var item);
        var service = new EvidencePreviewService(new Sha256HashService());

        var preview = await service.CreatePreviewAsync(packagePath, item, CancellationToken.None);

        Assert.Equal(EvidencePreviewKind.Image, preview.Kind);
        Assert.Equal(item.Sha256, preview.Sha256);
        await File.WriteAllBytesAsync(item.LocalEvidencePath, [0]);
        var tamperedPreview = await service.CreatePreviewAsync(packagePath, item, CancellationToken.None);
        Assert.Equal(EvidencePreviewKind.Unavailable, tamperedPreview.Kind);
    }

    private static FileRecoveryService CreateRecoveryService() =>
        new(new Sha256HashService(), NullLogger<FileRecoveryService>.Instance, minimumFreeSpaceReserveBytes: 0);

    private static string CreateEvidenceFile(
        string root,
        string relativePath,
        byte[] content,
        out AnalyzedEvidenceItem item)
    {
        var packagePath = Path.Combine(root, "evidence");
        var filesRoot = Path.Combine(packagePath, "files");
        Directory.CreateDirectory(packagePath);
        var evidencePath = EvidencePathUtility.ResolveInsideRoot(filesRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
        File.WriteAllBytes(evidencePath, content);
        var sourcePath = "/sdcard/" + relativePath.Replace('\\', '/');
        var timestamp = DateTimeOffset.UtcNow;
        var session = new AcquisitionSession
        {
            SessionId = "session-one",
            DeviceId = "test-device",
            DeviceSerial = "test-serial",
            DeviceManufacturer = "TestMaker",
            DeviceModel = "TestPhone",
            StartTimeUtc = timestamp,
            EndTimeUtc = timestamp,
            Status = AcquisitionStatus.Completed,
            DestinationPath = packagePath
        };
        var metadata = new EvidenceMetadata(
            new EvidenceApplication("AndroidRecovery", "1.0"),
            session,
            new EvidenceAcquisitionDetails("ADB", true, "SHA-256", [], true, true),
            null);
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        File.WriteAllText(Path.Combine(packagePath, "metadata.json"), JsonSerializer.Serialize(metadata, jsonOptions));
        item = new AnalyzedEvidenceItem("file-one", "session-one", sourcePath, relativePath,
            Path.GetFileName(relativePath), Path.GetExtension(relativePath), content.Length,
            Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(), DateTimeOffset.UtcNow,
            EvidenceFileType.Image, "PNG", AnalysisConfidence.High)
        {
            LocalEvidencePath = evidencePath,
            ConfidenceReason = "Test signature"
        };
        return packagePath;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}