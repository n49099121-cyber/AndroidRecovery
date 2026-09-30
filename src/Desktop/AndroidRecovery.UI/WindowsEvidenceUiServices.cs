using System.Diagnostics;
using AndroidRecovery.Recovery;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AndroidRecovery.UI;

public sealed class WindowsUserDestinationPicker(WindowHandleReference windowHandle) : IUserDestinationPicker
{
    public async Task<string?> PickFolderAsync(string? currentPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (windowHandle.Handle == 0)
        {
            throw new InvalidOperationException("The application window is not ready for a folder selection dialog.");
        }

        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            CommitButtonText = "Select evidence destination"
        };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, windowHandle.Handle);
        var folder = await picker.PickSingleFolderAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return folder?.Path;
    }
}

public sealed class WindowsEvidenceFolderOpener : IEvidenceFolderOpener
{
    public Task OpenFolderAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException("The evidence package directory does not exist.");
        }

        var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
        startInfo.ArgumentList.Add(fullPath);
        Process.Start(startInfo);
        return Task.CompletedTask;
    }
}