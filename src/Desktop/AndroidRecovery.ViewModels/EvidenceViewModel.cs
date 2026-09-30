using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AndroidRecovery.Recovery;
using Microsoft.Extensions.Logging;

namespace AndroidRecovery.ViewModels;

public sealed class EvidenceViewModel : INotifyPropertyChanged
{
    private readonly IEvidencePackageCatalog _catalog;
    private readonly IEvidenceVerifier _verifier;
    private readonly IEvidenceFolderOpener _folderOpener;
    private readonly ILogger<EvidenceViewModel> _logger;
    private EvidencePackageSummary? _selectedPackage;
    private string? _statusMessage;
    private bool _isBusy;

    public EvidenceViewModel(
        IEvidencePackageCatalog catalog,
        IEvidenceVerifier verifier,
        IEvidenceFolderOpener folderOpener,
        ILogger<EvidenceViewModel> logger)
    {
        _catalog = catalog;
        _verifier = verifier;
        _folderOpener = folderOpener;
        _logger = logger;
        RefreshCommand = new AsyncCommand(RefreshAsync, () => !IsBusy);
        OpenSelectedCommand = new AsyncCommand(OpenSelectedAsync, () => !IsBusy && SelectedPackage is not null);
        VerifySelectedCommand = new AsyncCommand(VerifySelectedAsync, () => !IsBusy && SelectedPackage is not null);
        AnalyzeSelectedCommand = new AsyncCommand(AnalyzeSelectedAsync, () => !IsBusy && SelectedPackage is not null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? AnalysisRequested;

    public ObservableCollection<EvidencePackageSummary> Packages { get; } = [];

    public bool HasPackages => Packages.Count > 0;

    public bool IsEmpty => !HasPackages;

    public bool HasSelectedPackage => SelectedPackage is not null;

    public string SelectedDeviceName => SelectedPackage is null
        ? "No package selected"
        : string.Join(" ", new[] { SelectedPackage.DeviceManufacturer, SelectedPackage.DeviceModel }
            .Where(part => !string.IsNullOrWhiteSpace(part)));

    public string SelectedAcquiredLabel => SelectedPackage?.AcquiredUtc.ToLocalTime().ToString("f") ?? "-";

    public string SelectedMethodLabel => SelectedPackage?.AcquisitionMethod ?? "-";

    public string SelectedSizeLabel => SelectedPackage is null ? "-" : FormatBytes(SelectedPackage.BytesAcquired);

    public string SelectedVerificationLabel => SelectedPackage?.VerificationLabel ?? "-";

    public AsyncCommand RefreshCommand { get; }

    public AsyncCommand OpenSelectedCommand { get; }

    public AsyncCommand VerifySelectedCommand { get; }

    public AsyncCommand AnalyzeSelectedCommand { get; }

    public EvidencePackageSummary? SelectedPackage
    {
        get => _selectedPackage;
        set
        {
            if (SetProperty(ref _selectedPackage, value))
            {
                OpenSelectedCommand.NotifyCanExecuteChanged();
                VerifySelectedCommand.NotifyCanExecuteChanged();
                AnalyzeSelectedCommand.NotifyCanExecuteChanged();
            }

            OnPropertyChanged(nameof(HasSelectedPackage));
            OnPropertyChanged(nameof(SelectedDeviceName));
            OnPropertyChanged(nameof(SelectedAcquiredLabel));
            OnPropertyChanged(nameof(SelectedMethodLabel));
            OnPropertyChanged(nameof(SelectedSizeLabel));
            OnPropertyChanged(nameof(SelectedVerificationLabel));
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshCommand.NotifyCanExecuteChanged();
                OpenSelectedCommand.NotifyCanExecuteChanged();
                VerifySelectedCommand.NotifyCanExecuteChanged();
                AnalyzeSelectedCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var selectedId = SelectedPackage?.SessionId;
            var packages = await _catalog.GetAllAsync(CancellationToken.None);
            Packages.Clear();
            foreach (var package in packages)
            {
                Packages.Add(package);
            }

            OnPropertyChanged(nameof(HasPackages));
            OnPropertyChanged(nameof(IsEmpty));

            SelectedPackage = selectedId is null
                ? Packages.FirstOrDefault()
                : Packages.FirstOrDefault(package => package.SessionId == selectedId) ?? Packages.FirstOrDefault();
            StatusMessage = Packages.Count == 0
                ? "No evidence packages are registered yet. A package appears here after an acquisition creates real evidence."
                : $"{Packages.Count:N0} evidence package(s) registered.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _logger.LogError("Evidence catalog could not be read ({ExceptionType})", exception.GetType().Name);
            StatusMessage = "The evidence catalog could not be read. Existing package files have not been changed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OpenSelectedAsync()
    {
        if (SelectedPackage is not null)
        {
            await _folderOpener.OpenFolderAsync(SelectedPackage.PackagePath, CancellationToken.None);
        }
    }

    private Task AnalyzeSelectedAsync()
    {
        if (SelectedPackage is not null)
        {
            AnalysisRequested?.Invoke(this, EventArgs.Empty);
        }

        return Task.CompletedTask;
    }

    private async Task VerifySelectedAsync()
    {
        if (SelectedPackage is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var verification = await _verifier.VerifyAsync(SelectedPackage.PackagePath, CancellationToken.None);
            SelectedPackage = SelectedPackage with { IsVerified = verification.Succeeded };
            await _catalog.RegisterAsync(SelectedPackage, CancellationToken.None);
            StatusMessage = verification.Succeeded
                ? $"Evidence verified. Files checked: {verification.FilesChecked:N0}; hashes: {verification.HashesVerified:N0}."
                : $"Evidence verification failed. Missing: {verification.MissingFiles:N0}; hash mismatches: {verification.HashMismatches:N0}; manifest errors: {verification.ManifestErrors:N0}.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _logger.LogError("Evidence verification could not be completed ({ExceptionType})", exception.GetType().Name);
            StatusMessage = "Evidence verification could not be completed. See diagnostics for details.";
        }
        finally
        {
            IsBusy = false;
        }
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
}