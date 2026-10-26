using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CodePublisher.Features.Deployment.CrashLogs;
using CodePublisher.Features.Deployment.Ios;

namespace CodePublisher.ViewModels;

/// <summary>
/// The crash log workspace of the Deploy section: it finds the crash reports of the app on the
/// selected target, shows what each one says, and copies the text so it can be pasted into an AI
/// agent.
///
/// The scan looks in every place a crash can be: the app's own data folder, the crash store the
/// operating system keeps, and the device log. Which of those can be read depends on the target,
/// and the summary says so instead of showing an empty list without a reason.
/// </summary>
public partial class MainViewModel
{
    public ObservableCollection<CrashLogRowViewModel> CrashLogs { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCrashLogsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyAllCrashLogsCommand))]
    private bool _isCrashLogsBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCrashLogs))]
    private string _crashLogsSummary = "Scan a target to look for the crash reports of the app.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCrashLogNotes))]
    private string _crashLogNotes = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCrashLogPreview))]
    private string _crashLogPreview = string.Empty;

    [ObservableProperty]
    private string _crashLogPreviewTitle = string.Empty;

    public bool HasCrashLogs => CrashLogs.Count > 0;

    public bool HasCrashLogNotes => !string.IsNullOrWhiteSpace(CrashLogNotes);

    public bool HasCrashLogPreview => !string.IsNullOrWhiteSpace(CrashLogPreview);

    /// <summary>Where the scan looks. The user needs to know which target was asked.</summary>
    public string CrashLogTargetText => SelectedCrashLogTarget switch
    {
        CrashLogTarget.AndroidDevice => SelectedDevice?.DisplayName ?? "the selected Android device",
        CrashLogTarget.IosSimulator => SelectedIosSimulator,
        CrashLogTarget.IosDevice => SelectedIosDevice?.DisplayName ?? "the selected iOS device",
        _ => "this computer"
    };

    private CrashLogTarget SelectedCrashLogTarget
    {
        get
        {
            if (IsAndroidPlatform)
            {
                return CrashLogTarget.AndroidDevice;
            }

            if (IsIosPlatform)
            {
                return SelectedIosDevice?.IsDeployable == true
                    ? CrashLogTarget.IosDevice
                    : CrashLogTarget.IosSimulator;
            }

            return CrashLogTarget.Desktop;
        }
    }

    /// <summary>
    /// Finds the crash reports of the app on the selected target. The scan only reads: it never
    /// writes to the device and never deletes a report.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanScanCrashLogs))]
    private async Task ScanCrashLogsAsync()
    {
        if (OperatingSystem.IsBrowser())
        {
            StatusMessage = "Crash logs are available in the desktop app.";
            return;
        }

        // A device needs the package id to tell its own crashes from the crashes of the other apps
        // on it, because they are stored in the same place. The button stays available and says
        // what is missing, the same way the other deploy actions do.
        if (!ReadsCrashLogsFromThisComputer && !TryValidatePackageId("Scan for crash logs"))
        {
            return;
        }

        try
        {
            IsCrashLogsBusy = true;
            CrashLogs.Clear();
            CrashLogPreview = string.Empty;
            CrashLogPreviewTitle = string.Empty;

            var scan = await RunCrashLogScanAsync();
            foreach (var artifact in scan.Artifacts)
            {
                CrashLogs.Add(new CrashLogRowViewModel(artifact, ShowCrashLogAsync, CopyCrashLogAsync));
            }

            CrashLogNotes = string.Join(Environment.NewLine, scan.Notes);
            CrashLogsSummary = scan.Artifacts.Count == 0
                ? $"No crash report for {CrashLogAppName} was found on {CrashLogTargetText}."
                : $"Found {scan.Artifacts.Count} crash report(s) for {CrashLogAppName} on {CrashLogTargetText}.";
            StatusMessage = CrashLogsSummary;
            OnPropertyChanged(nameof(HasCrashLogs));
        }
        catch (Exception ex)
        {
            CrashLogs.Clear();
            CrashLogsSummary = "The crash report scan failed.";
            StatusMessage = "The crash report scan failed.";
            await _desktopInteractionService.ShowErrorAsync("Crash logs", ex.Message);
        }
        finally
        {
            IsCrashLogsBusy = false;
        }
    }

    private bool CanScanCrashLogs() =>
        !OperatingSystem.IsBrowser() && !IsCrashLogsBusy;

    /// <summary>
    /// True when the reports are read from this computer instead of from a target. A desktop app
    /// has no package id, and the crash reports of the operating system need no target.
    /// </summary>
    private bool ReadsCrashLogsFromThisComputer => !IsAndroidPlatform && !IsIosPlatform;

    /// <summary>The app the reports belong to. On a device that is the package id.</summary>
    private string CrashLogAppName => IsAndroidPlatform || IsIosPlatform
        ? PackageId
        : _publisherService.FindDesktopAppName(OutputDirectory) ?? "the published app";

    private Task<CrashLogScan> RunCrashLogScanAsync()
    {
        switch (SelectedCrashLogTarget)
        {
            case CrashLogTarget.AndroidDevice:
                if (SelectedDevice is null || !SelectedDevice.IsReady)
                {
                    throw new InvalidOperationException("Select an Android device first. The crash reports are read from the device.");
                }

                return _publisherService.ScanAndroidCrashLogsAsync(SelectedDevice.Serial, PackageId.Trim(), CancellationToken.None);

            case CrashLogTarget.IosDevice:
                if (SelectedIosDevice is null || !SelectedIosDevice.IsDeployable)
                {
                    throw new InvalidOperationException("Select a connected iPhone or iPad first.");
                }

                return _publisherService.ScanIosDeviceCrashLogsAsync(SelectedIosDevice.Udid, PackageId.Trim(), CancellationToken.None);

            case CrashLogTarget.IosSimulator:
                if (string.IsNullOrWhiteSpace(SelectedIosSimulator))
                {
                    throw new InvalidOperationException("Select an iOS simulator first. The crash reports are read from the simulator.");
                }

                return _publisherService.ScanIosSimulatorCrashLogsAsync(
                    IosSimulatorService.ExtractSimulatorId(SelectedIosSimulator),
                    PackageId.Trim(),
                    CancellationToken.None);

            default:
                return _publisherService.ScanDesktopCrashLogsAsync(
                    _publisherService.FindDesktopAppName(OutputDirectory),
                    CancellationToken.None);
        }
    }

    private Task ShowCrashLogAsync(CrashLogRowViewModel row)
    {
        CrashLogPreviewTitle = row.Name;
        CrashLogPreview = row.Artifact.CopyableContent;
        StatusMessage = row.CanShowText
            ? $"Showing {row.Name}."
            : $"{row.Name} is a binary dump, so it cannot be shown as text.";
        return Task.CompletedTask;
    }

    private async Task CopyCrashLogAsync(CrashLogRowViewModel row)
    {
        await _desktopInteractionService.CopyTextToClipboardAsync(CrashLogReport.BuildEntry(row.Artifact));
        StatusMessage = $"{row.Name} copied. Paste it into an AI agent to read the crash.";
        PlayCompletionAlert(failed: false);
    }

    /// <summary>
    /// Copies every report in one block, newest first. This is the paste the user wants when the
    /// app has crashed more than once and the whole story is needed.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCopyAllCrashLogs))]
    private async Task CopyAllCrashLogsAsync()
    {
        var artifacts = CrashLogs.Select(row => row.Artifact).ToArray();
        if (artifacts.Length == 0)
        {
            StatusMessage = "There is no crash report to copy. Scan a target first.";
            return;
        }

        var text = CrashLogReport.BuildAll(
            artifacts,
            $"Crash reports of {CrashLogAppName} on {CrashLogTargetText}.");

        await _desktopInteractionService.CopyTextToClipboardAsync(text);
        StatusMessage = $"{artifacts.Length} crash report(s) copied. Paste them into an AI agent to read the crashes.";
        PlayCompletionAlert(failed: false);
    }

    private bool CanCopyAllCrashLogs() =>
        !OperatingSystem.IsBrowser() && !IsCrashLogsBusy && CrashLogs.Count > 0;
}

internal enum CrashLogTarget
{
    AndroidDevice,
    IosSimulator,
    IosDevice,
    Desktop
}
