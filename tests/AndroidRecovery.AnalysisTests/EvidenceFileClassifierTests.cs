using System.Text.Json;
using System.Text.Json.Serialization;
using AndroidRecovery.FileSystem;
using AndroidRecovery.Models;
using AndroidRecovery.Recovery;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AndroidRecovery.AnalysisTests;

public sealed class EvidenceFileClassifierTests
{
    [Fact]
    public void Classify_UsesPngSignatureInsteadOfExtension()
    {
        var classification = EvidenceFileClassifier.Classify("image.bin",
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        Assert.Equal(EvidenceFileType.Image, classification.Type);
        Assert.Equal("PNG", classification.Format);
        Assert.Equal(AnalysisConfidence.High, classification.Confidence);
    }

    [Fact]
    public void Classify_DoesNotTrustImageExtensionWithoutMatchingContent()
    {
        var classification = EvidenceFileClassifier.Classify("photo.jpg", [0x01, 0x02, 0x03, 0x00]);

        Assert.Equal(EvidenceFileType.Unknown, classification.Type);
        Assert.Equal(AnalysisConfidence.Unknown, classification.Confidence);
    }

    [Fact]
    public void Classify_RecognizesOfficeDocumentContainer()
    {
        var classification = EvidenceFileClassifier.Classify("report.docx", "PK\x03\x04"u8);

        Assert.Equal(EvidenceFileType.Document, classification.Type);
        Assert.Equal("Office Open XML", classification.Format);
    }

    [Fact]
    public void Classify_RecognizesSqliteDatabaseSignature()
    {
        var classification = EvidenceFileClassifier.Classify("records.bin", "SQLite format 3\0"u8);

        Assert.Equal(EvidenceFileType.Database, classification.Type);
        Assert.Equal("SQLite", classification.Format);
    }

    [Fact]
    public void Classify_RecognizesReadableUtf8Text()
    {
        var classification = EvidenceFileClassifier.Classify("notes.unknown", "ordinary text\r\n"u8);

        Assert.Equal(EvidenceFileType.Text, classification.Type);
        Assert.Equal(AnalysisConfidence.Medium, classification.Confidence);
    }

    [Fact]
    public async Task AnalyzeAsync_RefusesAnUnverifiedEvidencePackage()
    {
        using var workspace = new TemporaryDirectory();
        var verifier = new FixedEvidenceVerifier(false);
        var service = new EvidenceAnalysisService(verifier, NullLogger<EvidenceAnalysisService>.Instance);

        var result = await service.AnalyzeAsync(workspace.Path, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(result.EvidenceVerified);
        Assert.Empty(result.Items);
        Assert.Equal(1, verifier.CallCount);
    }

    [Fact]
    public async Task AnalyzeAsync_ClassifiesVerifiedFilesAndPreservesProvenance()
    {
        using var workspace = new TemporaryDirectory();
        var sourcePath = Path.Combine(workspace.Path, "files", "Pictures", "image.png");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllBytesAsync(sourcePath, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "manifest.json"),
            JsonSerializer.Serialize(CreateManifest(workspace.Path), CreateJsonOptions()));
        var service = new EvidenceAnalysisService(new FixedEvidenceVerifier(true),
            NullLogger<EvidenceAnalysisService>.Instance);

        var result = await service.AnalyzeAsync(workspace.Path, null, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.True(result.Succeeded);
        Assert.True(result.EvidenceVerified);
        Assert.Equal("session-test", item.SessionId);
        Assert.Equal("/sdcard/Pictures/image.png", item.SourcePath);
        Assert.Equal("Pictures\\image.png", item.RelativePath);
        Assert.Equal("aabbcc", item.Sha256);
        Assert.Equal(EvidenceFileType.Image, item.Type);
        Assert.Equal(AnalysisConfidence.High, item.Confidence);
    }

    private static EvidenceManifest CreateManifest(string packagePath)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var session = new AcquisitionSession
        {
            SessionId = "session-test",
            DeviceId = "test-device",
            DeviceSerial = "test-serial",
            StartTimeUtc = timestamp,
            EndTimeUtc = timestamp,
            Status = AcquisitionStatus.Completed,
            DestinationPath = packagePath,
            TotalFilesDiscovered = 1,
            TotalFilesAcquired = 1,
            TotalBytesDiscovered = 8,
            TotalBytesAcquired = 8
        };
        var file = new EvidenceFileRecord
        {
            Id = "file-test",
            SessionId = session.SessionId,
            Category = AcquisitionCategory.Pictures,
            SourcePath = "/sdcard/Pictures/image.png",
            RelativePath = "Pictures\\image.png",
            FileName = "image.png",
            Extension = ".png",
            SizeBytes = 8,
            Sha256 = "aabbcc",
            AcquiredUtc = timestamp,
            Status = AcquiredFileStatus.Acquired
        };
        return new EvidenceManifest(
            new EvidenceApplication("AndroidRecovery", "1.0"),
            session,
            new EvidenceAcquisitionDetails("ADB", true, "SHA-256", [AcquisitionCategory.Pictures], true, true),
            [file], [], [], null);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed class FixedEvidenceVerifier(bool succeeded) : IEvidenceVerifier
    {
        public int CallCount { get; private set; }

        public Task<EvidenceVerificationResult> VerifyAsync(string packagePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(new EvidenceVerificationResult
            {
                Succeeded = succeeded,
                FilesChecked = succeeded ? 1 : 0,
                HashesVerified = succeeded ? 1 : 0,
                Messages = succeeded ? ["Verified"] : ["Verification failed"]
            });
        }
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