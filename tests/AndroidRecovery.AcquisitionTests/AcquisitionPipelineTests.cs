using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using AndroidRecovery.Adb;
using AndroidRecovery.Models;
using AndroidRecovery.Recovery;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AndroidRecovery.AcquisitionTests;

public sealed class AcquisitionPipelineTests
{
    [Fact]
    public async Task AdbEnumerator_PreservesNewlinesAndUnicodeFromNulDelimitedFindOutput()
    {
        const string sourcePath = "/sdcard/Pictures/line\nbreak_資料.jpg";
        var adb = new FakeAdbService([1, 2, 3]) { FindPaths = [sourcePath] };
        var enumerator = new AdbDeviceFileEnumerator(adb, NullLogger<AdbDeviceFileEnumerator>.Instance);

        var result = await enumerator.EnumerateAsync(AuthorizedDevice(),
            new HashSet<AcquisitionCategory> { AcquisitionCategory.Pictures }, null, "session", CancellationToken.None);

        var file = Assert.Single(result.Files);
        Assert.Equal(sourcePath, file.SourcePath);
        Assert.Equal("Pictures\\line_break_資料.jpg", file.RelativePath);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void AndroidShellPath_QuotesSingleQuotesWithoutAllowingArgumentBreakout()
    {
        Assert.Equal("'/sdcard/space path/a'\\''b.jpg'", AndroidShellPath.Quote("/sdcard/space path/a'b.jpg"));
    }

    [Fact]
    public async Task AcquireAsync_CopiesHashesRecordsAndVerifiesEvidencePackage()
    {
        using var workspace = new TemporaryDirectory();
        var content = System.Text.Encoding.UTF8.GetBytes("public test image bytes");
        var sourceFile = new DeviceFile("/sdcard/Pictures/test.jpg", "Pictures/test.jpg", content.Length);
        var provider = CreateProvider(workspace.Path, [sourceFile], content);
        var options = CreateOptions(workspace.Path);
        var progressValues = new List<AcquisitionProgress>();

        var result = await provider.AcquireAsync(AuthorizedDevice(), options,
            new InlineProgress<AcquisitionProgress>(progressValues.Add), CancellationToken.None);

        Assert.True(result.Verification?.Succeeded,
            string.Join(Environment.NewLine, result.Verification?.Messages ?? []));
        Assert.Equal(AcquisitionStatus.Completed, result.Status);
        Assert.True(result.Success);
        Assert.NotNull(result.EvidencePackage);
        Assert.NotNull(result.Verification);
        Assert.True(result.Verification.Succeeded);
        Assert.Equal(1, result.Session.TotalFilesDiscovered);
        Assert.Equal(1, result.Session.TotalFilesAcquired);
        Assert.Equal(content.Length, result.Session.TotalBytesAcquired);
        Assert.Contains(progressValues, progress => progress.FilesAcquired == 1);
        Assert.True(File.Exists(result.EvidencePackage.MetadataPath));
        Assert.True(File.Exists(result.EvidencePackage.ManifestPath));
        Assert.True(File.Exists(result.EvidencePackage.ManifestDatabasePath));
        Assert.True(File.Exists(result.EvidencePackage.AcquisitionLogPath));
        Assert.True(File.Exists(result.EvidencePackage.ReportPath));

        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(result.EvidencePackage.ManifestPath));
        var acquiredFile = manifest.RootElement.GetProperty("files")[0];
        Assert.Equal("Pictures\\test.jpg", acquiredFile.GetProperty("relativePath").GetString());
        var expectedHash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        Assert.Equal(expectedHash, acquiredFile.GetProperty("sha256").GetString());
    }

    [Fact]
    public async Task AcquireAsync_UsesDeterministicCollisionNamesWithoutOverwriting()
    {
        using var workspace = new TemporaryDirectory();
        var first = new DeviceFile("/sdcard/DCIM/Camera/photo.jpg", "DCIM/Camera/photo.jpg", 3);
        var second = new DeviceFile("/sdcard/Pictures/photo.jpg", "DCIM/Camera/photo.jpg", 3);
        var provider = CreateProvider(workspace.Path, [first, second], [1, 2, 3]);

        var result = await provider.AcquireAsync(AuthorizedDevice(), CreateOptions(workspace.Path), null, CancellationToken.None);

        Assert.Equal(AcquisitionStatus.Completed, result.Status);
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(result.EvidencePackage!.ManifestPath));
        var paths = manifest.RootElement.GetProperty("files").EnumerateArray()
            .Select(file => file.GetProperty("relativePath").GetString()).ToArray();
        Assert.Collection(paths,
            path => Assert.Equal("DCIM\\Camera\\photo.jpg", path),
            path => Assert.Equal("DCIM\\Camera\\photo (1).jpg", path));
    }

    [Fact]
    public async Task AcquireAsync_WhenFolderPreservationIsDisabledUsesCategoryDirectory()
    {
        using var workspace = new TemporaryDirectory();
        var source = new DeviceFile("/sdcard/DCIM/Camera/photo.jpg", "DCIM/Camera/photo.jpg", 3);
        var provider = CreateProvider(workspace.Path, [source], [1, 2, 3]);
        var options = CreateOptions(workspace.Path) with
        {
            PreserveFolderStructure = false,
            SelectedCategories = new HashSet<AcquisitionCategory> { AcquisitionCategory.Photos }
        };

        var result = await provider.AcquireAsync(AuthorizedDevice(), options, null, CancellationToken.None);

        Assert.Equal(AcquisitionStatus.Completed, result.Status);
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(result.EvidencePackage!.ManifestPath));
        Assert.Equal("Photos\\photo.jpg", manifest.RootElement.GetProperty("files")[0]
            .GetProperty("relativePath").GetString());
    }

    [Fact]
    public async Task AcquireAsync_PreservesPackageAndReportsRecoverablePullFailure()
    {
        using var workspace = new TemporaryDirectory();
        var enumerator = new FakeEnumerator(
        [
            new DeviceFile("/sdcard/Pictures/good.jpg", "Pictures/good.jpg", 3),
            new DeviceFile("/sdcard/Pictures/bad.jpg", "Pictures/bad.jpg", 3)
        ]);
        var adb = new FakeAdbService([1, 2, 3]) { FailedSourcePath = "/sdcard/Pictures/bad.jpg" };
        var provider = CreateProvider(workspace.Path, enumerator, adb);

        var result = await provider.AcquireAsync(AuthorizedDevice(), CreateOptions(workspace.Path), null, CancellationToken.None);

        Assert.Equal(AcquisitionStatus.CompletedWithWarnings, result.Status);
        Assert.Single(result.Errors);
        Assert.Equal(1, result.Session.TotalFilesAcquired);
        Assert.True(File.Exists(result.EvidencePackage!.ManifestPath));
        Assert.True(result.Verification!.Succeeded);
    }

    [Fact]
    public async Task AcquireAsync_RejectsUnauthorizedDeviceBeforeCreatingPackage()
    {
        using var workspace = new TemporaryDirectory();
        var provider = CreateProvider(workspace.Path, [], [1]);
        var unauthorized = AuthorizedDevice() with { AdbState = AdbDeviceState.Unauthorized };

        var result = await provider.AcquireAsync(unauthorized, CreateOptions(workspace.Path), null, CancellationToken.None);

        Assert.Equal(AcquisitionStatus.Failed, result.Status);
        Assert.Equal("DeviceUnauthorized", Assert.Single(result.Errors).ErrorCode);
        Assert.False(Directory.Exists(Path.Combine(workspace.Path, "AndroidRecovery")));
    }

    [Fact]
    public async Task AcquireAsync_RejectsMissingDevice()
    {
        using var workspace = new TemporaryDirectory();
        var provider = CreateProvider(workspace.Path, [], [1]);

        var result = await provider.AcquireAsync(null!, CreateOptions(workspace.Path), null, CancellationToken.None);

        Assert.Equal("NoDeviceSelected", Assert.Single(result.Errors).ErrorCode);
        Assert.Equal(AcquisitionStatus.Failed, result.Status);
    }

    [Fact]
    public async Task AcquireAsync_RejectsNoCategories()
    {
        using var workspace = new TemporaryDirectory();
        var provider = CreateProvider(workspace.Path, [], [1]);
        var options = CreateOptions(workspace.Path) with { SelectedCategories = new HashSet<AcquisitionCategory>() };

        var result = await provider.AcquireAsync(AuthorizedDevice(), options, null, CancellationToken.None);

        Assert.Equal("NoCategoriesSelected", Assert.Single(result.Errors).ErrorCode);
    }

    [Fact]
    public async Task AcquireAsync_RejectsOfflineDevice()
    {
        using var workspace = new TemporaryDirectory();
        var provider = CreateProvider(workspace.Path, [], [1]);

        var result = await provider.AcquireAsync(AuthorizedDevice() with { AdbState = AdbDeviceState.Offline },
            CreateOptions(workspace.Path), null, CancellationToken.None);

        Assert.Equal("DeviceOffline", Assert.Single(result.Errors).ErrorCode);
    }

    [Fact]
    public async Task AcquireAsync_RejectsUnavailableAdb()
    {
        using var workspace = new TemporaryDirectory();
        var adb = new FakeAdbService([1]) { Available = false };
        var provider = CreateProvider(workspace.Path, new FakeEnumerator([]), adb);

        var result = await provider.AcquireAsync(AuthorizedDevice(), CreateOptions(workspace.Path), null, CancellationToken.None);

        Assert.Equal("AdbUnavailable", Assert.Single(result.Errors).ErrorCode);
    }

    [Fact]
    public async Task AcquireAsync_RejectsEmptyDestination()
    {
        using var workspace = new TemporaryDirectory();
        var provider = CreateProvider(workspace.Path, [], [1]);
        var options = CreateOptions(workspace.Path) with { DestinationPath = "" };

        var result = await provider.AcquireAsync(AuthorizedDevice(), options, null, CancellationToken.None);

        Assert.Equal("DestinationRequired", Assert.Single(result.Errors).ErrorCode);
    }

    [Fact]
    public async Task AcquireAsync_RejectsInsufficientSpaceBeforePullingFiles()
    {
        using var workspace = new TemporaryDirectory();
        var adb = new FakeAdbService([1, 2, 3]);
        var file = new DeviceFile("/sdcard/Pictures/large.jpg", "Pictures/large.jpg", long.MaxValue);
        var provider = CreateProvider(workspace.Path, new FakeEnumerator([file]), adb);

        var result = await provider.AcquireAsync(AuthorizedDevice(), CreateOptions(workspace.Path), null, CancellationToken.None);

        Assert.Equal(AcquisitionStatus.Failed, result.Status);
        Assert.Equal("InsufficientDestinationSpace", Assert.Single(result.Errors).ErrorCode);
        Assert.Equal(0, adb.PullCount);
        Assert.NotNull(result.EvidencePackage);
        Assert.True(File.Exists(result.EvidencePackage.ManifestPath));
        Assert.True(result.Verification?.Succeeded);
    }

    [Fact]
    public async Task AcquireAsync_CancelledBeforeStartReturnsCancelledWithoutPackage()
    {
        using var workspace = new TemporaryDirectory();
        var provider = CreateProvider(workspace.Path, [], [1]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await provider.AcquireAsync(
            AuthorizedDevice(), CreateOptions(workspace.Path), null, cancellation.Token);

        Assert.Equal(AcquisitionStatus.Cancelled, result.Status);
        Assert.Null(result.EvidencePackage);
    }

    [Fact]
    public async Task EvidenceVerifier_DetectsMissingAcquiredFiles()
    {
        using var workspace = new TemporaryDirectory();
        var content = new byte[] { 4, 5, 6 };
        var provider = CreateProvider(workspace.Path,
            [new DeviceFile("/sdcard/Pictures/test.jpg", "Pictures/test.jpg", content.Length)], content);
        var acquisition = await provider.AcquireAsync(AuthorizedDevice(), CreateOptions(workspace.Path), null, CancellationToken.None);
        var filePath = Path.Combine(acquisition.EvidencePackage!.FilesPath, "Pictures", "test.jpg");
        File.Delete(filePath);

        var verifier = new EvidenceVerifier(new Sha256HashService(), NullLogger<EvidenceVerifier>.Instance);
        var verification = await verifier.VerifyAsync(acquisition.EvidencePackage.RootPath, CancellationToken.None);

        Assert.False(verification.Succeeded);
        Assert.Equal(1, verification.MissingFiles);
    }

    [Fact]
    public async Task EvidenceVerifier_DetectsTamperedAcquiredFiles()
    {
        using var workspace = new TemporaryDirectory();
        var content = new byte[] { 4, 5, 6 };
        var provider = CreateProvider(workspace.Path,
            [new DeviceFile("/sdcard/Pictures/test.jpg", "Pictures/test.jpg", content.Length)], content);
        var acquisition = await provider.AcquireAsync(AuthorizedDevice(), CreateOptions(workspace.Path), null, CancellationToken.None);
        var filePath = Path.Combine(acquisition.EvidencePackage!.FilesPath, "Pictures", "test.jpg");
        await File.WriteAllBytesAsync(filePath, [9, 9, 9]);

        var verifier = new EvidenceVerifier(new Sha256HashService(), NullLogger<EvidenceVerifier>.Instance);
        var verification = await verifier.VerifyAsync(acquisition.EvidencePackage.RootPath, CancellationToken.None);

        Assert.False(verification.Succeeded);
        Assert.Equal(1, verification.HashMismatches);
    }

    [Fact]
    public async Task EvidenceVerifier_DetectsDatabaseFileMetadataMismatch()
    {
        using var workspace = new TemporaryDirectory();
        var content = new byte[] { 4, 5, 6 };
        var provider = CreateProvider(workspace.Path,
            [new DeviceFile("/sdcard/Pictures/test.jpg", "Pictures/test.jpg", content.Length)], content);
        var acquisition = await provider.AcquireAsync(AuthorizedDevice(), CreateOptions(workspace.Path), null, CancellationToken.None);

        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = acquisition.EvidencePackage!.ManifestDatabasePath,
            Pooling = false
        }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE evidence_files SET source_path=$source_path WHERE session_id=$session_id;";
            command.Parameters.AddWithValue("$source_path", "/sdcard/Pictures/other.jpg");
            command.Parameters.AddWithValue("$session_id", acquisition.Session.SessionId);
            await command.ExecuteNonQueryAsync();
        }

        var verifier = new EvidenceVerifier(new Sha256HashService(), NullLogger<EvidenceVerifier>.Instance);
        var verification = await verifier.VerifyAsync(acquisition.EvidencePackage!.RootPath, CancellationToken.None);

        Assert.False(verification.Succeeded);
        Assert.True(verification.ManifestErrors > 0);
    }

    [Fact]
    public async Task AcquireAsync_CancellationDuringPullPreservesEarlierAcquiredFiles()
    {
        using var workspace = new TemporaryDirectory();
        var firstFile = new DeviceFile("/sdcard/Pictures/first.jpg", "Pictures/first.jpg", 3);
        var secondFile = new DeviceFile("/sdcard/Pictures/second.jpg", "Pictures/second.jpg", 3);
        var pullStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pullGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var adb = new FakeAdbService([1, 2, 3])
        {
            BlockingSourcePath = secondFile.SourcePath,
            PullStartedSignal = pullStarted,
            PullGate = pullGate
        };
        var provider = CreateProvider(workspace.Path, new FakeEnumerator([firstFile, secondFile]), adb);
        using var cancellation = new CancellationTokenSource();

        var acquisitionTask = provider.AcquireAsync(AuthorizedDevice(), CreateOptions(workspace.Path), null, cancellation.Token);
        await pullStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        var result = await acquisitionTask;

        Assert.Equal(AcquisitionStatus.Cancelled, result.Status);
        Assert.Equal(1, result.Session.TotalFilesAcquired);
        Assert.NotNull(result.EvidencePackage);
        Assert.True(File.Exists(Path.Combine(result.EvidencePackage.FilesPath, "Pictures", "first.jpg")));
        Assert.True(result.Verification!.Succeeded);
    }

    private static AdbAcquisitionProvider CreateProvider(string root, IReadOnlyList<DeviceFile> files, byte[] content) =>
        CreateProvider(root, new FakeEnumerator(files), new FakeAdbService(content));

    private static AdbAcquisitionProvider CreateProvider(string root, FakeEnumerator enumerator, FakeAdbService adb)
    {
        var hash = new Sha256HashService();
        var logger = NullLogger<AdbAcquisitionProvider>.Instance;
        var writerFactory = new EvidencePackageWriterFactory(NullLogger<EvidencePackageWriterFactory>.Instance);
        var verifier = new EvidenceVerifier(hash, NullLogger<EvidenceVerifier>.Instance);
        return new AdbAcquisitionProvider(adb, enumerator, writerFactory, verifier, hash, logger,
            minimumFreeSpaceReserveBytes: 0);
    }

    private static AcquisitionOptions CreateOptions(string root) => new()
    {
        SelectedCategories = new HashSet<AcquisitionCategory> { AcquisitionCategory.Pictures },
        DestinationPath = root
    };

    private static AndroidDevice AuthorizedDevice() => new(
        "test-serial", "TestMaker", "TestPhone", "test_product", "15", 35, AdbDeviceState.Device);

    private sealed class FakeEnumerator(IReadOnlyList<DeviceFile> files) : IDeviceFileEnumerator
    {
        public Task<FileEnumerationResult> EnumerateAsync(
            AndroidDevice device,
            IReadOnlySet<AcquisitionCategory> categories,
            IProgress<AcquisitionProgress>? progress,
            string sessionId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new FileEnumerationResult(files, [], []));
        }
    }

    private sealed class FakeAdbService(byte[] content) : IAdbService
    {
        private int _pullCount;

        public string? FailedSourcePath { get; init; }

        public IReadOnlyList<string> FindPaths { get; init; } = [];

        public bool Available { get; init; } = true;

        public string? BlockingSourcePath { get; init; }

        public TaskCompletionSource<bool>? PullStartedSignal { get; init; }

        public TaskCompletionSource<bool>? PullGate { get; init; }

        public int PullCount => Volatile.Read(ref _pullCount);

        public bool IsAvailable => Available;

        public string ExecutablePath => "fake-adb";

        public Task<AdbCommandResult> ExecuteAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (arguments.Count > 0 && arguments[0] == "devices")
            {
                return Task.FromResult(new AdbCommandResult(true, "List of devices attached\ntest-serial device\n", "", 0));
            }

            if (arguments.Contains("stat", StringComparer.Ordinal))
            {
                return Task.FromResult(new AdbCommandResult(true, "3\n", "", 0));
            }

            return Task.FromResult(new AdbCommandResult(false, "", "Unexpected test command.", 1, "UnexpectedCommand"));
        }

        public Task<AdbCommandResult> ExecuteLinesAsync(
            IReadOnlyList<string> arguments,
            Func<string, CancellationToken, ValueTask> onLine,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AdbCommandResult(false, "", "Unexpected test enumeration command.", 1));

        public async Task<AdbCommandResult> ExecuteBytesAsync(
            IReadOnlyList<string> arguments,
            Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> onBytes,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var output = Encoding.UTF8.GetBytes(string.Join('\0', FindPaths) + '\0');
            for (var offset = 0; offset < output.Length; offset += 3)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = Math.Min(3, output.Length - offset);
                await onBytes(output.AsMemory(offset, count), cancellationToken);
            }

            return new AdbCommandResult(true, "", "", 0);
        }

        public async Task<AdbCommandResult> PullFileAsync(
            string deviceSerial,
            string sourcePath,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _pullCount);
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(sourcePath, FailedSourcePath, StringComparison.Ordinal))
            {
                return new AdbCommandResult(false, "", "Permission denied", 1, "AdbCommandFailed");
            }

            if (string.Equals(sourcePath, BlockingSourcePath, StringComparison.Ordinal))
            {
                PullStartedSignal?.TrySetResult(true);
                await PullGate!.Task.WaitAsync(cancellationToken);
            }

            await using var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous);
            await output.WriteAsync(content, cancellationToken);
            return new AdbCommandResult(true, "", "", 0);
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
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