# Android Data Recovery Tool

AndroidRecovery is a read-only-first Windows desktop application for authorized Android device acquisition and analysis. The current milestone supports ADB device discovery and acquisition of accessible public shared-storage files into a verifiable evidence package. Acquisition is not deleted-file recovery; Android-private app data, SMS, call history, encrypted data, and inaccessible files are not bypassed.

## Requirements

- Windows 10 19041 or Windows 11, x64
- .NET 8 SDK
- Visual Studio with the Windows App SDK/WinUI 3 and MSIX packaging build tools
- Android SDK Platform Tools from a trusted source

Place trusted Android SDK Platform Tools under `tools/platform-tools/` in the repository. The WinUI build stages `adb.exe` and its companion DLLs beside the application at the configured `tools/platform-tools/adb.exe` path. The app does not download binaries or search `PATH`. ADB commands are argument-based; enumeration is restricted to configured `/sdcard` shared-storage folders and pull operations require the selected device to remain authorized.

By default, evidence is written under `Documents/AndroidRecovery/Evidence/AndroidRecovery_<timestamp>_<session-id>/`. Each package includes `metadata.json`, `manifest.json`, `manifest.db`, `acquisition.log`, `files/`, and `reports/acquisition-report.json`. The app-local evidence index is `%LOCALAPPDATA%\AndroidRecovery\evidence-index.json`.

From Evidence, select a package and choose **Analyze** to verify the package before classifying acquired files by content signature. Results supports debounced case-insensitive search, category/type/format/confidence/hash-integrity/support filters, size bounds, sorting, multi-selection, and source/hash provenance. Image previews are loaded on demand at a bounded decode width; UTF-8 text previews are limited to 64 KiB. Other types show an explicit preview-unavailable message. Structural corruption detection and rich media/document parsing are not implemented, so results do not claim files are structurally healthy.

Choose **Recover Selected** to export selected evidence files to a user-chosen folder outside the source evidence package. Export streams files, defaults to deterministic keep-both naming, checks available space with a 512 MiB reserve, verifies source and output SHA-256, and writes `recovery-report.json` with acquisition/device provenance. Export does not access the Android device or modify the evidence package. Structural corruption detection, rich media/document parsing, thumbnails, database record analysis, contacts/messages/call-log analysis, persistent recovery history, and PDF reports are not implemented.

## Build and test

Build the WinUI app with the VS Code task **Build WinUI app with Visual Studio MSBuild**. Launch with the **Launch AndroidRecovery App** task. The WinUI build uses Visual Studio MSBuild so the Windows App SDK resource-packaging tasks are available.

Run the test projects individually with:

```powershell
dotnet test tests/AndroidRecovery.UnitTests/AndroidRecovery.UnitTests.csproj -c Debug
dotnet test tests/AndroidRecovery.IntegrationTests/AndroidRecovery.IntegrationTests.csproj -c Debug
dotnet test tests/AndroidRecovery.RecoveryTests/AndroidRecovery.RecoveryTests.csproj -c Debug
dotnet test tests/AndroidRecovery.AcquisitionTests/AndroidRecovery.AcquisitionTests.csproj -c Debug
dotnet test tests/AndroidRecovery.AnalysisTests/AndroidRecovery.AnalysisTests.csproj -c Debug
```

## Manual device test

1. On an authorized phone, enable Developer Options and USB debugging through Android Settings.
2. Connect it by USB, unlock it, and accept the Android USB debugging authorization prompt.
3. Confirm the device is shown as ADB authorized on Devices. Do not attempt to authorize it from AndroidRecovery.
4. From Home, choose Start Recovery and select one or more supported shared-storage categories.
5. Choose Next. If no authorized phone is selected, choose the device in Devices and continue.
6. Review the selected device and categories, choose a destination with enough free space, and start acquisition.
7. Keep the device connected; progress reflects enumerated files, copied bytes, and hashes. Cancel to check that acquired files remain in the package.
8. Open Evidence, select the package, and run Verify. Confirm metadata, SQLite, file records, and recorded SHA-256 values. Choose Analyze to verify again and browse signature-classified files.
9. In Results, preview a supported image/text item, select files, and choose Recover Selected. Use a destination with enough space and outside the evidence package; inspect exported hashes and `recovery-report.json`.

The automated tests use deterministic fake ADB/enumeration sources and do not require a phone. Multiple vendors/Android versions and physical disconnect behavior still require manual device testing.

Logs are written under `%LOCALAPPDATA%\AndroidRecovery\logs`. Device identifiers and recovered/private content are not written to application logs.