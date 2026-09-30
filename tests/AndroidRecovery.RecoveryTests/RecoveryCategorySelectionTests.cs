using AndroidRecovery.DeviceDetection;
using AndroidRecovery.Models;
using AndroidRecovery.Recovery;
using AndroidRecovery.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AndroidRecovery.RecoveryTests;

public sealed class RecoveryCategorySelectionTests
{
    [Fact]
    public void ContinueRequiresSelectionAndSelectAllClearAllUpdateCommandState()
    {
        var deviceViewModel = new DeviceViewModel(new EmptyDeviceDetectionService(),
            NullLogger<DeviceViewModel>.Instance);
        var viewModel = new RecoveryViewModel(deviceViewModel, new UnusedAcquisitionProvider(),
            new EmptyEvidenceCatalog(), Options.Create(new AcquisitionConfiguration()),
            new NoDestinationPicker(), new NoFolderOpener(), NullLogger<RecoveryViewModel>.Instance);
        var continueRequests = 0;
        viewModel.ContinueRequested += (_, _) => continueRequests++;

        Assert.False(viewModel.CanContinue);
        Assert.False(viewModel.ContinueToAcquisitionCommand.CanExecute(null));

        viewModel.Categories[0].IsSelected = true;
        Assert.True(viewModel.CanContinue);
        Assert.True(viewModel.ContinueToAcquisitionCommand.CanExecute(null));
        viewModel.ContinueToAcquisitionCommand.Execute(null);
        Assert.Equal(1, continueRequests);

        viewModel.ClearAllCategoriesCommand.Execute(null);
        Assert.False(viewModel.CanContinue);
        Assert.False(viewModel.ContinueToAcquisitionCommand.CanExecute(null));

        viewModel.SelectAllCategoriesCommand.Execute(null);
        Assert.True(viewModel.CanContinue);
        Assert.All(viewModel.Categories, category => Assert.True(category.IsSelected));
    }

    private sealed class EmptyDeviceDetectionService : IDeviceDetectionService
    {
        public Task<DeviceDetectionResult> GetDevicesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new DeviceDetectionResult(false, [], "No device"));
    }

    private sealed class UnusedAcquisitionProvider : IAcquisitionProvider
    {
        public string Name => "Unused test provider";

        public Task<AcquisitionResult> AcquireAsync(AndroidDevice device, AcquisitionOptions options,
            IProgress<AcquisitionProgress>? progress, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Acquisition is not part of this ViewModel test.");
    }

    private sealed class EmptyEvidenceCatalog : IEvidencePackageCatalog
    {
        public Task<IReadOnlyList<EvidencePackageSummary>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EvidencePackageSummary>>([]);

        public Task RegisterAsync(EvidencePackageSummary summary, CancellationToken cancellationToken) =>
            Task.CompletedTask;
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