using AndroidRecovery.Recovery;
using Xunit;

namespace AndroidRecovery.RecoveryTests;

public sealed class EvidencePackageCatalogTests
{
    [Fact]
    public async Task RegisterAsync_PersistsSummaryAndReplacesSameSession()
    {
        using var workspace = new TemporaryDirectory();
        var packagePath = CreatePackageDirectory(workspace.Path, "package-one");
        var catalog = new JsonEvidencePackageCatalog(Path.Combine(workspace.Path, "evidence-index.json"));

        await catalog.RegisterAsync(CreateSummary("session-one", packagePath, fileCount: 2), CancellationToken.None);
        await catalog.RegisterAsync(CreateSummary("session-one", packagePath, fileCount: 3), CancellationToken.None);

        var summary = Assert.Single(await catalog.GetAllAsync(CancellationToken.None));
        Assert.Equal(3, summary.FileCount);
        Assert.Equal("session-one", summary.SessionId);
    }

    [Fact]
    public async Task GetAllAsync_FiltersPackagesWhoseDirectoryWasRemoved()
    {
        using var workspace = new TemporaryDirectory();
        var packagePath = CreatePackageDirectory(workspace.Path, "package-one");
        var catalog = new JsonEvidencePackageCatalog(Path.Combine(workspace.Path, "evidence-index.json"));
        await catalog.RegisterAsync(CreateSummary("session-one", packagePath, fileCount: 2), CancellationToken.None);
        Directory.Delete(packagePath, recursive: true);

        Assert.Empty(await catalog.GetAllAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetAllAsync_LabelsPackageMissingManifestAsIncomplete()
    {
        using var workspace = new TemporaryDirectory();
        var packagePath = CreatePackageDirectory(workspace.Path, "partial-package");
        var catalog = new JsonEvidencePackageCatalog(Path.Combine(workspace.Path, "evidence-index.json"));
        await catalog.RegisterAsync(CreateSummary("session-partial", packagePath, fileCount: 4) with
        {
            IsVerified = false
        }, CancellationToken.None);

        var summary = Assert.Single(await catalog.GetAllAsync(CancellationToken.None));

        Assert.False(summary.IsComplete);
        Assert.Equal("Incomplete", summary.VerificationLabel);
    }

    private static string CreatePackageDirectory(string root, string name)
    {
        var path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "metadata.json"), "{}");
        return path;
    }

    private static EvidencePackageSummary CreateSummary(string sessionId, string packagePath, long fileCount) => new()
    {
        SessionId = sessionId,
        PackagePath = packagePath,
        DeviceManufacturer = "TestMaker",
        DeviceModel = "TestPhone",
        AcquiredUtc = DateTimeOffset.UtcNow,
        FileCount = fileCount,
        BytesAcquired = fileCount * 1024,
        Status = AcquisitionStatus.Completed,
        IsVerified = true
    };

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