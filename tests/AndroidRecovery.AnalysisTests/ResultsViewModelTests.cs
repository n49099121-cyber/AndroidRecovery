using AndroidRecovery.Core;
using AndroidRecovery.Models;
using AndroidRecovery.Recovery;
using AndroidRecovery.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AndroidRecovery.AnalysisTests;

public sealed class ResultsViewModelTests
{
    [Fact]
    public void FiltersAndSortingCombineAndSelectionReportsCountAndSize()
    {
        var photo = CreateItem("photo", "photo.jpg", EvidenceFileType.Image, "JPEG", 300, AnalysisConfidence.High);
        var video = CreateItem("video", "clip.mp4", EvidenceFileType.Video, "ISO Base Media", 900, AnalysisConfidence.Medium);
        var document = CreateItem("document", "notes.txt", EvidenceFileType.Text, "UTF-8 text", 100, AnalysisConfidence.Medium);
        var viewModel = CreateViewModel([photo, video, document]);

        viewModel.CategoryFilter = "Photos";
        viewModel.TypeFilter = "Image";
        viewModel.ConfidenceFilter = "High";
        Assert.Equal("photo", Assert.Single(viewModel.FilteredItems).Id);

        viewModel.CategoryFilter = "All";
        viewModel.TypeFilter = "All";
        viewModel.ConfidenceFilter = "All";
        viewModel.SortField = "Size";
        Assert.Equal(["document", "photo", "video"], viewModel.FilteredItems.Select(item => item.Id));

        viewModel.SetSelectedItems([photo, document]);
        Assert.Equal(2, viewModel.SelectedCount);
        Assert.Equal(400, viewModel.SelectedSizeBytes);
        Assert.True(viewModel.CanExportSelected);

        viewModel.SortDescending = true;
        Assert.Equal(["video", "photo", "document"], viewModel.FilteredItems.Select(item => item.Id));
    }

    [Fact]
    public void CorruptItemsCannotBeSelectedForExport()
    {
        var corrupt = CreateItem("corrupt", "bad.jpg", EvidenceFileType.Image, "JPEG", 12,
            AnalysisConfidence.Low) with { IsCorrupt = true };
        var viewModel = CreateViewModel([corrupt]);

        viewModel.SetSelectedItems([corrupt]);

        Assert.Equal(0, viewModel.SelectedCount);
        Assert.False(viewModel.CanExportSelected);
    }

    [Fact]
    public void FormatSizeIntegrityAndSupportFiltersCombine()
    {
        var photo = CreateItem("photo", "photo.jpg", EvidenceFileType.Image, "JPEG", 300, AnalysisConfidence.High);
        var other = CreateItem("other", "payload.bin", EvidenceFileType.Unknown, "Unrecognized signature", 100,
            AnalysisConfidence.Unknown);
        var corrupt = CreateItem("corrupt", "broken.png", EvidenceFileType.Image, "PNG", 200,
            AnalysisConfidence.Low) with { IsCorrupt = true };
        var viewModel = CreateViewModel([photo, other, corrupt]);

        viewModel.FormatFilter = "JPEG";
        viewModel.MinimumSizeText = "250";
        viewModel.MaximumSizeText = "350";
        viewModel.IntegrityFilter = "Hash verified";
        viewModel.SupportFilter = "Supported";

        Assert.Equal("photo", Assert.Single(viewModel.FilteredItems).Id);

        viewModel.FormatFilter = "All";
        viewModel.MinimumSizeText = "";
        viewModel.MaximumSizeText = "";
        viewModel.IntegrityFilter = "All";
        viewModel.SupportFilter = "Unsupported";
        Assert.Equal("other", Assert.Single(viewModel.FilteredItems).Id);
    }

    [Fact]
    public async Task SearchIsDebouncedAndCaseInsensitiveAcrossSourcePath()
    {
        var photo = CreateItem("photo", "photo.jpg", EvidenceFileType.Image, "JPEG", 300, AnalysisConfidence.High);
        var viewModel = CreateViewModel([photo]);
        viewModel.SearchText = "dcim/camera";

        await Task.Delay(350);

        Assert.Equal("photo", Assert.Single(viewModel.FilteredItems).Id);
    }

    private static AnalysisViewModel CreateViewModel(IReadOnlyList<AnalyzedEvidenceItem> items)
    {
        var viewModel = new AnalysisViewModel(new FixedAnalysisService(items), new UnusedPreviewService(),
            new UnusedRecoveryService(), new NoDestinationPicker(), new NoFolderOpener(),
            NullLogger<AnalysisViewModel>.Instance);
        viewModel.SetPackagePath(Path.Combine(Path.GetTempPath(), "test-evidence"));
        viewModel.AnalyzeCommand.Execute(null);
        return viewModel;
    }

    private static AnalyzedEvidenceItem CreateItem(
        string id,
        string fileName,
        EvidenceFileType type,
        string format,
        long size,
        AnalysisConfidence confidence) => new(
            id, "session-test", "/sdcard/DCIM/Camera/" + fileName, "DCIM/Camera/" + fileName,
            fileName, Path.GetExtension(fileName), size, new string('a', 64), DateTimeOffset.UtcNow,
            type, format, confidence)
        {
            LocalEvidencePath = Path.Combine("test-evidence", "files", "DCIM", "Camera", fileName),
            ConfidenceReason = "Unit-test signature"
        };

    private sealed class FixedAnalysisService(IReadOnlyList<AnalyzedEvidenceItem> items) : IAnalysisService
    {
        public Task<EvidenceAnalysisResult> AnalyzeAsync(string packagePath, IProgress<AnalysisProgress>? progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new EvidenceAnalysisResult("session-test", packagePath, true, true, items, []));
        }
    }

    private sealed class UnusedPreviewService : IPreviewService
    {
        public Task<EvidencePreviewDetails> CreatePreviewAsync(string packagePath, AnalyzedEvidenceItem item,
            CancellationToken cancellationToken) => throw new InvalidOperationException("Preview is not part of this test.");
    }

    private sealed class UnusedRecoveryService : IRecoveryService
    {
        public Task<RecoveryResult> ExportAsync(string evidencePackagePath, IReadOnlyList<AnalyzedEvidenceItem> items,
            string destinationPath, IProgress<RecoveryProgress>? progress, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Export is not part of this test.");
    }

    private sealed class NoDestinationPicker : IUserDestinationPicker
    {
        public Task<string?> PickFolderAsync(string? currentPath, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }

    private sealed class NoFolderOpener : IEvidenceFolderOpener
    {
        public Task OpenFolderAsync(string path, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}