using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AndroidRecovery.Core;
using AndroidRecovery.Models;
using AndroidRecovery.Recovery;
using Microsoft.Extensions.Logging;

namespace AndroidRecovery.ViewModels;

public sealed class AnalysisViewModel : INotifyPropertyChanged
{
    private readonly IAnalysisService _analysisService;
    private readonly IPreviewService _previewService;
    private readonly IRecoveryService _recoveryService;
    private readonly IUserDestinationPicker _destinationPicker;
    private readonly IEvidenceFolderOpener _folderOpener;
    private readonly ILogger<AnalysisViewModel> _logger;
    private CancellationTokenSource? _analysisCancellation;
    private CancellationTokenSource? _exportCancellation;
    private CancellationTokenSource? _searchCancellation;
    private string? _packagePath;
    private string _searchText = "";
    private string _debouncedSearchText = "";
    private string _categoryFilter = "All";
    private string _typeFilter = "All";
    private string _confidenceFilter = "All";
    private string _formatFilter = "All";
    private string _integrityFilter = "All";
    private string _supportFilter = "All";
    private string _minimumSizeText = "";
    private string _maximumSizeText = "";
    private string _sortField = "Name";
    private bool _sortDescending;
    private string? _statusMessage;
    private AnalysisProgress? _progress;
    private EvidenceAnalysisResult? _result;
    private EvidencePreviewDetails? _previewDetails;
    private RecoveryProgress? _recoveryProgress;
    private RecoveryResult? _recoveryResult;
    private HashSet<string> _selectedIds = new(StringComparer.Ordinal);
    private bool _isBusy;
    private bool _isAnalyzing;
    private bool _isExporting;

    public AnalysisViewModel(
        IAnalysisService analysisService,
        IPreviewService previewService,
        IRecoveryService recoveryService,
        IUserDestinationPicker destinationPicker,
        IEvidenceFolderOpener folderOpener,
        ILogger<AnalysisViewModel> logger)
    {
        _analysisService = analysisService;
        _previewService = previewService;
        _recoveryService = recoveryService;
        _destinationPicker = destinationPicker;
        _folderOpener = folderOpener;
        _logger = logger;
        AnalyzeCommand = new AsyncCommand(AnalyzeAsync, () => CanAnalyze);
        CancelCommand = new AsyncCommand(CancelAsync, () => IsBusy);
        SelectAllVisibleCommand = new AsyncCommand(SelectAllVisibleAsync, () => HasResults && !IsBusy);
        ClearSelectionCommand = new AsyncCommand(ClearSelectionAsync, () => SelectedCount > 0 && !IsBusy);
        PreviewSelectedCommand = new AsyncCommand(PreviewSelectedAsync, () => SelectedCount == 1 && !IsBusy);
        ExportSelectedCommand = new AsyncCommand(ExportSelectedAsync, () => CanExportSelected);
        CancelExportCommand = new AsyncCommand(CancelExportAsync, () => IsBusy && _exportCancellation is not null);
        OpenExportFolderCommand = new AsyncCommand(OpenExportFolderAsync,
            () => RecoveryResult is not null && !string.IsNullOrWhiteSpace(RecoveryResult.OutputFolderPath));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AsyncCommand AnalyzeCommand { get; }

    public AsyncCommand CancelCommand { get; }

    public AsyncCommand SelectAllVisibleCommand { get; }

    public AsyncCommand ClearSelectionCommand { get; }

    public AsyncCommand PreviewSelectedCommand { get; }

    public AsyncCommand ExportSelectedCommand { get; }

    public AsyncCommand CancelExportCommand { get; }

    public AsyncCommand OpenExportFolderCommand { get; }

    public string? PackagePath
    {
        get => _packagePath;
        private set => SetProperty(ref _packagePath, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                _ = UpdateDebouncedSearchAsync(value);
            }
        }
    }

    public string CategoryFilter
    {
        get => _categoryFilter;
        set => SetFilter(ref _categoryFilter, value);
    }

    public string TypeFilter
    {
        get => _typeFilter;
        set => SetFilter(ref _typeFilter, value);
    }

    public string ConfidenceFilter
    {
        get => _confidenceFilter;
        set => SetFilter(ref _confidenceFilter, value);
    }

    public string FormatFilter
    {
        get => _formatFilter;
        set => SetFilter(ref _formatFilter, value);
    }

    public string IntegrityFilter
    {
        get => _integrityFilter;
        set => SetFilter(ref _integrityFilter, value);
    }

    public string SupportFilter
    {
        get => _supportFilter;
        set => SetFilter(ref _supportFilter, value);
    }

    public string MinimumSizeText
    {
        get => _minimumSizeText;
        set => SetFilter(ref _minimumSizeText, value);
    }

    public string MaximumSizeText
    {
        get => _maximumSizeText;
        set => SetFilter(ref _maximumSizeText, value);
    }

    public string SortField
    {
        get => _sortField;
        set => SetFilter(ref _sortField, value);
    }

    public bool SortDescending
    {
        get => _sortDescending;
        set
        {
            if (SetProperty(ref _sortDescending, value))
            {
                OnPropertyChanged(nameof(FilteredItems));
            }
        }
    }

    public IReadOnlyList<string> CategoryFilters { get; } = ["All", "Photos", "Videos", "Audio", "Documents", "Other"];

    public IReadOnlyList<string> TypeFilters { get; } = ["All", "Image", "Video", "Audio", "Document", "Archive", "Database", "Text", "Unknown"];

    public IReadOnlyList<string> ConfidenceFilters { get; } = ["All", "High", "Medium", "Low", "Unknown"];

    public IReadOnlyList<string> IntegrityFilters { get; } = ["All", "Hash verified", "Corrupt"];

    public IReadOnlyList<string> SupportFilters { get; } = ["All", "Supported", "Unsupported"];

    public IReadOnlyList<string> FormatFilters => Result is null
        ? ["All"]
        : ["All", .. Result.Items.Select(item => item.DetectedFormat)
            .Where(format => !string.IsNullOrWhiteSpace(format)).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(format => format, StringComparer.OrdinalIgnoreCase)];

    public IReadOnlyList<string> SortFields { get; } = ["Name", "Size", "Date", "Type", "Category", "Confidence"];

    public IReadOnlyList<string> CategoryCountLabels => CategoryFilters
        .Select(category => $"{category} ({GetCategoryCount(category):N0})").ToArray();

    public string ResultCountSummary => $"{FilteredItems.Count:N0} results | {SelectedCount:N0} selected";

    public int SelectedCount => _selectedIds.Count;

    public long SelectedSizeBytes => SelectedItems.Aggregate(0L, (total, item) =>
        total > long.MaxValue - Math.Max(0, item.SizeBytes) ? long.MaxValue : total + Math.Max(0, item.SizeBytes));

    public string SelectedSizeLabel => FormatBytes(SelectedSizeBytes);

    public IReadOnlyList<AnalyzedEvidenceItem> SelectedItems => Result?.Items
        .Where(item => _selectedIds.Contains(item.Id) && item.IsCorrupt != true).ToArray() ?? [];

    public bool CanExportSelected => !IsBusy && Result?.EvidenceVerified == true && SelectedItems.Count > 0;

    public bool IsAnalyzing => _isAnalyzing;

    public bool IsExporting => _isExporting;

    public EvidencePreviewDetails? PreviewDetails
    {
        get => _previewDetails;
        private set
        {
            if (SetProperty(ref _previewDetails, value))
            {
                OnPropertyChanged(nameof(HasPreview));
                OnPropertyChanged(nameof(ShowImagePreview));
                OnPropertyChanged(nameof(ShowTextPreview));
                OnPropertyChanged(nameof(ShowUnavailablePreview));
                OnPropertyChanged(nameof(PreviewSizeLabel));
            }
        }
    }

    public bool HasPreview => PreviewDetails is not null;

    public bool ShowImagePreview => PreviewDetails?.Kind == EvidencePreviewKind.Image;

    public bool ShowTextPreview => PreviewDetails?.Kind == EvidencePreviewKind.Text;

    public bool ShowUnavailablePreview => PreviewDetails?.Kind is EvidencePreviewKind.Unsupported or EvidencePreviewKind.Unavailable;

    public string PreviewSizeLabel => PreviewDetails is null ? "" : FormatBytes(PreviewDetails.SizeBytes);

    public RecoveryProgress? RecoveryProgress
    {
        get => _recoveryProgress;
        private set
        {
            if (SetProperty(ref _recoveryProgress, value))
            {
                OnPropertyChanged(nameof(RecoveryProgressSummary));
            }
        }
    }

    public string RecoveryProgressSummary => RecoveryProgress is null
        ? ""
        : $"{RecoveryProgress.FilesCompleted:N0} of {RecoveryProgress.FilesTotal:N0} files | {FormatBytes(RecoveryProgress.BytesCompleted)} of {FormatBytes(RecoveryProgress.BytesTotal)}";

    public RecoveryResult? RecoveryResult
    {
        get => _recoveryResult;
        private set
        {
            if (SetProperty(ref _recoveryResult, value))
            {
                OnPropertyChanged(nameof(HasRecoveryResult));
                OnPropertyChanged(nameof(ShowResultsToolbar));
                OnPropertyChanged(nameof(RecoveryResultSummary));
                OnPropertyChanged(nameof(RecoveryMessages));
                OpenExportFolderCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasRecoveryResult => RecoveryResult is not null;

    public bool ShowResultsToolbar => HasResults && !HasRecoveryResult;

    public string RecoveryResultSummary => RecoveryResult is null
        ? ""
        : $"{RecoveryResult.SuccessfulCount:N0} verified | {RecoveryResult.FailedCount:N0} failed | {RecoveryResult.CancelledCount:N0} cancelled | {FormatBytes(RecoveryResult.BytesExported)} exported";

    public string RecoveryMessages => RecoveryResult is null
        ? ""
        : string.Join(Environment.NewLine, RecoveryResult.Errors.Concat(RecoveryResult.Warnings));

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public AnalysisProgress? Progress
    {
        get => _progress;
        private set
        {
            if (SetProperty(ref _progress, value))
            {
                OnPropertyChanged(nameof(ProgressSummary));
                OnPropertyChanged(nameof(CurrentPath));
            }
        }
    }

    public EvidenceAnalysisResult? Result
    {
        get => _result;
        private set
        {
            if (SetProperty(ref _result, value))
            {
                OnPropertyChanged(nameof(FilteredItems));
                OnPropertyChanged(nameof(HasResults));
                OnPropertyChanged(nameof(HasNoMatchingItems));
                OnPropertyChanged(nameof(ResultCountSummary));
                OnPropertyChanged(nameof(CategoryCountLabels));
                OnPropertyChanged(nameof(FormatFilters));
                OnPropertyChanged(nameof(CanExportSelected));
                OnPropertyChanged(nameof(SelectedItems));
                OnPropertyChanged(nameof(SelectedCount));
                OnPropertyChanged(nameof(SelectedSizeBytes));
                OnPropertyChanged(nameof(SelectedSizeLabel));
                OnPropertyChanged(nameof(VerificationLabel));
                OnPropertyChanged(nameof(WarningSummary));
                SelectAllVisibleCommand.NotifyCanExecuteChanged();
                ExportSelectedCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanAnalyze));
                AnalyzeCommand.NotifyCanExecuteChanged();
                CancelCommand.NotifyCanExecuteChanged();
                SelectAllVisibleCommand.NotifyCanExecuteChanged();
                ClearSelectionCommand.NotifyCanExecuteChanged();
                PreviewSelectedCommand.NotifyCanExecuteChanged();
                ExportSelectedCommand.NotifyCanExecuteChanged();
                CancelExportCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool CanAnalyze => !IsBusy && !string.IsNullOrWhiteSpace(PackagePath);

    public bool HasResults => Result?.Succeeded == true && FilteredItems.Count > 0;

    public bool HasNoMatchingItems => Result?.Succeeded == true && FilteredItems.Count == 0;

    public IReadOnlyList<AnalyzedEvidenceItem> FilteredItems
    {
        get
        {
            IEnumerable<AnalyzedEvidenceItem> items = Result?.Items ?? [];
            if (CategoryFilter != "All") items = items.Where(item => GetCategory(item) == CategoryFilter);
            if (TypeFilter != "All") items = items.Where(item => item.Type.ToString() == TypeFilter);
            if (FormatFilter != "All") items = items.Where(item => item.DetectedFormat.Equals(FormatFilter, StringComparison.OrdinalIgnoreCase));
            if (ConfidenceFilter != "All") items = items.Where(item => item.Confidence.ToString() == ConfidenceFilter);
            if (IntegrityFilter == "Hash verified") items = items.Where(item => Result?.EvidenceVerified == true && !string.IsNullOrWhiteSpace(item.Sha256));
            if (IntegrityFilter == "Corrupt") items = items.Where(item => item.IsCorrupt == true);
            if (SupportFilter == "Supported") items = items.Where(item => item.IsSupported);
            if (SupportFilter == "Unsupported") items = items.Where(item => !item.IsSupported);
            if (long.TryParse(MinimumSizeText, out var minimumSize)) items = items.Where(item => item.SizeBytes >= minimumSize);
            if (long.TryParse(MaximumSizeText, out var maximumSize)) items = items.Where(item => item.SizeBytes <= maximumSize);
            if (!string.IsNullOrWhiteSpace(_debouncedSearchText))
            {
                items = items.Where(item => item.FileName.Contains(_debouncedSearchText, StringComparison.OrdinalIgnoreCase)
                    || item.SourcePath.Contains(_debouncedSearchText, StringComparison.OrdinalIgnoreCase)
                    || item.RelativePath.Contains(_debouncedSearchText, StringComparison.OrdinalIgnoreCase)
                    || item.Type.ToString().Contains(_debouncedSearchText, StringComparison.OrdinalIgnoreCase)
                    || item.DetectedFormat.Contains(_debouncedSearchText, StringComparison.OrdinalIgnoreCase));
            }

            items = SortField switch
            {
                "Size" => SortDescending ? items.OrderByDescending(item => item.SizeBytes) : items.OrderBy(item => item.SizeBytes),
                "Date" => SortDescending ? items.OrderByDescending(item => item.AcquiredUtc) : items.OrderBy(item => item.AcquiredUtc),
                "Type" => SortDescending ? items.OrderByDescending(item => item.Type) : items.OrderBy(item => item.Type),
                "Category" => SortDescending ? items.OrderByDescending(GetCategory) : items.OrderBy(GetCategory),
                "Confidence" => SortDescending ? items.OrderByDescending(item => item.Confidence) : items.OrderBy(item => item.Confidence),
                _ => SortDescending ? items.OrderByDescending(item => item.FileName, StringComparer.OrdinalIgnoreCase)
                    : items.OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
            };
            return items.ToArray();
        }
    }

    public string ProgressSummary => Progress is null
        ? ""
        : Progress.FilesTotal > 0
            ? $"{Progress.FilesCompleted:N0} of {Progress.FilesTotal:N0} files classified"
            : Progress.Phase;

    public string CurrentPath => Progress?.CurrentRelativePath ?? "";

    public string VerificationLabel => Result is null
        ? "Evidence not yet verified"
        : Result.EvidenceVerified ? "Evidence verified before analysis" : "Evidence verification failed";

    public string WarningSummary => Result is null || Result.Warnings.Count == 0
        ? ""
        : $"{Result.Warnings.Count:N0} file(s) could not be analyzed.";

    public void SetSelectedItems(IEnumerable<AnalyzedEvidenceItem> items)
    {
        _selectedIds = items.Where(item => item.IsCorrupt != true).Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        OnPropertyChanged(nameof(SelectedItems));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedSizeBytes));
        OnPropertyChanged(nameof(SelectedSizeLabel));
        OnPropertyChanged(nameof(ResultCountSummary));
        OnPropertyChanged(nameof(CanExportSelected));
        ClearSelectionCommand.NotifyCanExecuteChanged();
        PreviewSelectedCommand.NotifyCanExecuteChanged();
        ExportSelectedCommand.NotifyCanExecuteChanged();
    }

    public async Task PreviewAsync(AnalyzedEvidenceItem item)
    {
        if (Result?.EvidenceVerified != true || PackagePath is null || item.IsCorrupt == true)
        {
            return;
        }

        try
        {
            PreviewDetails = await _previewService.CreatePreviewAsync(PackagePath, item, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.LogWarning("Evidence preview failed ({ExceptionType})", exception.GetType().Name);
            PreviewDetails = new(item.FileName, EvidencePreviewKind.Unavailable, item.DetectedFormat,
                item.SizeBytes, item.SourcePath, item.LocalEvidencePath, item.Sha256,
                item.Confidence, item.ConfidenceReason, null, "This evidence file could not be previewed safely.");
        }
    }

    public void ClearPreview() => PreviewDetails = null;

    public void SetPackagePath(string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        if (IsBusy)
        {
            return;
        }

        if (string.Equals(PackagePath, packagePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        PackagePath = packagePath;
        _selectedIds.Clear();
        OnPropertyChanged(nameof(SelectedItems));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedSizeBytes));
        OnPropertyChanged(nameof(SelectedSizeLabel));
        Result = null;
        RecoveryResult = null;
        PreviewDetails = null;
        Progress = null;
        SearchText = "";
        _debouncedSearchText = "";
        StatusMessage = "Ready to verify and analyze this evidence package.";
        OnPropertyChanged(nameof(CanAnalyze));
        AnalyzeCommand.NotifyCanExecuteChanged();
    }

    private async Task AnalyzeAsync()
    {
        if (!CanAnalyze || PackagePath is null)
        {
            return;
        }

        IsBusy = true;
        _isAnalyzing = true;
        OnPropertyChanged(nameof(IsAnalyzing));
        _selectedIds.Clear();
        Result = null;
        Progress = null;
        StatusMessage = "Verifying package before analysis.";
        _analysisCancellation = new CancellationTokenSource();

        try
        {
            var progress = new Progress<AnalysisProgress>(value => Progress = value);
            Result = await _analysisService.AnalyzeAsync(PackagePath, progress, _analysisCancellation.Token);
            StatusMessage = Result.Succeeded
                ? $"Analyzed {Result.Items.Count:N0} evidence file(s). {WarningSummary}"
                : "Analysis stopped because evidence verification did not pass.";
        }
        catch (OperationCanceledException) when (_analysisCancellation.IsCancellationRequested)
        {
            StatusMessage = "Analysis cancelled. The evidence package was not modified.";
        }
        catch (Exception exception)
        {
            _logger.LogError("Evidence analysis failed ({ExceptionType})", exception.GetType().Name);
            StatusMessage = "Analysis could not be completed. The evidence package was not modified.";
        }
        finally
        {
            _analysisCancellation.Dispose();
            _analysisCancellation = null;
            _isAnalyzing = false;
            OnPropertyChanged(nameof(IsAnalyzing));
            IsBusy = false;
        }
    }

    private Task CancelAsync()
    {
        _analysisCancellation?.Cancel();
        return Task.CompletedTask;
    }

    private Task SelectAllVisibleAsync()
    {
        var selectedIds = _selectedIds.Concat(FilteredItems.Select(item => item.Id)).ToHashSet(StringComparer.Ordinal);
        SetSelectedItems(Result?.Items.Where(item => selectedIds.Contains(item.Id)) ?? []);
        return Task.CompletedTask;
    }

    private Task ClearSelectionAsync()
    {
        SetSelectedItems([]);
        return Task.CompletedTask;
    }

    private Task PreviewSelectedAsync() => SelectedItems.Count == 1
        ? PreviewAsync(SelectedItems[0])
        : Task.CompletedTask;

    private async Task ExportSelectedAsync()
    {
        if (!CanExportSelected || PackagePath is null)
        {
            return;
        }

        var destinationPath = await _destinationPicker.PickFolderAsync(null, CancellationToken.None);
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            return;
        }

        IsBusy = true;
        _isExporting = true;
        OnPropertyChanged(nameof(IsExporting));
        RecoveryProgress = null;
        RecoveryResult = null;
        _exportCancellation = new CancellationTokenSource();
        StatusMessage = "Exporting selected evidence files and verifying each copy.";
        try
        {
            var progress = new Progress<RecoveryProgress>(value => RecoveryProgress = value);
            RecoveryResult = await _recoveryService.ExportAsync(PackagePath, SelectedItems, destinationPath,
                progress, _exportCancellation.Token);
            StatusMessage = RecoveryResult.Status switch
            {
                RecoverySessionStatus.Completed => "Export completed. All exported files passed SHA-256 verification.",
                RecoverySessionStatus.CompletedWithWarnings => "Export completed with file or verification warnings.",
                RecoverySessionStatus.Cancelled => "Export cancelled. Previously verified files were preserved.",
                _ => "Export failed. Review the details before trying again."
            };
        }
        catch (OperationCanceledException) when (_exportCancellation.IsCancellationRequested)
        {
            StatusMessage = "Export cancelled. Previously verified files were preserved.";
        }
        catch (Exception exception)
        {
            _logger.LogError("Evidence export failed ({ExceptionType})", exception.GetType().Name);
            StatusMessage = "Export failed. Check the destination and try again.";
        }
        finally
        {
            _exportCancellation?.Dispose();
            _exportCancellation = null;
            _isExporting = false;
            OnPropertyChanged(nameof(IsExporting));
            IsBusy = false;
        }
    }

    private Task CancelExportAsync()
    {
        _exportCancellation?.Cancel();
        return Task.CompletedTask;
    }

    private async Task OpenExportFolderAsync()
    {
        if (RecoveryResult is not null && !string.IsNullOrWhiteSpace(RecoveryResult.OutputFolderPath))
        {
            await _folderOpener.OpenFolderAsync(RecoveryResult.OutputFolderPath, CancellationToken.None);
        }
    }

    private async Task UpdateDebouncedSearchAsync(string value)
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellation.Token);
            if (ReferenceEquals(_searchCancellation, cancellation))
            {
                _debouncedSearchText = value;
                OnPropertyChanged(nameof(FilteredItems));
                OnPropertyChanged(nameof(HasNoMatchingItems));
                OnPropertyChanged(nameof(HasResults));
                OnPropertyChanged(nameof(ShowResultsToolbar));
                OnPropertyChanged(nameof(ResultCountSummary));
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_searchCancellation, cancellation))
            {
                _searchCancellation = null;
                cancellation.Dispose();
            }
        }
    }

    private void SetFilter(ref string field, string value)
    {
        if (SetProperty(ref field, value))
        {
            OnPropertyChanged(nameof(FilteredItems));
            OnPropertyChanged(nameof(HasNoMatchingItems));
            OnPropertyChanged(nameof(HasResults));
            OnPropertyChanged(nameof(ShowResultsToolbar));
            OnPropertyChanged(nameof(ResultCountSummary));
            SelectAllVisibleCommand.NotifyCanExecuteChanged();
        }
    }

    private int GetCategoryCount(string category) => Result?.Items.Count(item =>
        category == "All" || GetCategory(item) == category) ?? 0;

    private static string GetCategory(AnalyzedEvidenceItem item) => item.Type switch
    {
        EvidenceFileType.Image => "Photos",
        EvidenceFileType.Video => "Videos",
        EvidenceFileType.Audio => "Audio",
        EvidenceFileType.Document => "Documents",
        _ => "Other"
    };

    private static string FormatBytes(long bytes)
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}