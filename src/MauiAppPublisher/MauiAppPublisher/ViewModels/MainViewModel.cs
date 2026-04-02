using System.ComponentModel;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotNetAppPublisher.Models;
using DotNetAppPublisher.Services;

namespace DotNetAppPublisher.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private static readonly HashSet<string> PreviewSensitiveProperties =
    [
        nameof(PublishPlatform),
        nameof(ProjectDirectory),
        nameof(TargetFramework),
        nameof(RuntimeIdentifier),
        nameof(Configuration),
        nameof(OutputDirectory),
        nameof(IncludeApk),
        nameof(IncludeAab),
        nameof(SelfContained),
        nameof(PublishTrimmed),
        nameof(PublishAot),
        nameof(PublishReadyToRun),
        nameof(PublishSingleFile),
        nameof(UseAppHost),
        nameof(CreateMacAppBundle),
        nameof(CreateWindowsExecutable),
        nameof(BuildIpa),
        nameof(ArchiveOnBuild),
        nameof(RunAotCompilation),
        nameof(EnableProfiledAot),
        nameof(AndroidLinkMode),
        nameof(AndroidLinkTool),
        nameof(AndroidDexTool),
        nameof(CreateMappingFile),
        nameof(EnableMultiDex),
        nameof(UseAapt2),
        nameof(EnableDesugar),
        nameof(SignMode),
        nameof(KeystorePath),
        nameof(KeyAlias),
        nameof(KeystorePassword),
        nameof(KeyPassword)
    ];

    private readonly StringBuilder _logBuilder = new();
    private readonly DesktopInteractionService _desktopInteractionService;
    private readonly PublisherService _publisherService;

    public MainViewModel(PublisherService publisherService, DesktopInteractionService desktopInteractionService)
    {
        _publisherService = publisherService;
        _desktopInteractionService = desktopInteractionService;
        DotnetStatus = publisherService.DotnetStatusText;
        AdbStatus = publisherService.AdbStatusText;
        EmulatorStatus = publisherService.EmulatorStatusText;
        StatusMessage = "Select a .NET project folder to start building your publish command.";
        RefreshCommandPreview();
        _ = RefreshEmulatorsAsync();
    }

    public IReadOnlyList<string> PublishPlatformOptions { get; } =
    [
        PublisherService.AndroidPlatform,
        PublisherService.MacOsPlatform,
        PublisherService.WindowsPlatform,
        PublisherService.IosPlatform
    ];

    public IReadOnlyList<string> ConfigurationOptions { get; } = ["Release", "Debug"];

    public IReadOnlyList<string> LinkModeOptions { get; } = ["None", "SdkOnly", "Full"];

    public IReadOnlyList<string> LinkToolOptions { get; } = ["r8", "proguard"];

    public IReadOnlyList<string> DexToolOptions { get; } = ["d8", "dx"];

    public IReadOnlyList<string> SignModeOptions { get; } = ["Auto", "Sign", "Do Not Sign"];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallLatestApkCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(LaunchAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteDesktopAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshEmulatorsCommand))]
    [NotifyCanExecuteChangedFor(nameof(LaunchEmulatorCommand))]
    [NotifyPropertyChangedFor(nameof(IsAndroidPlatform))]
    [NotifyPropertyChangedFor(nameof(IsMacOsPlatform))]
    [NotifyPropertyChangedFor(nameof(IsAndroidActionsVisible))]
    [NotifyPropertyChangedFor(nameof(IsDesktopActionsVisible))]
    [NotifyPropertyChangedFor(nameof(IsAndroidPublishOptionsVisible))]
    [NotifyPropertyChangedFor(nameof(IsMacOsPublishOptionsVisible))]
    [NotifyPropertyChangedFor(nameof(IsWindowsPublishOptionsVisible))]
    [NotifyPropertyChangedFor(nameof(IsIosPublishOptionsVisible))]
    [NotifyPropertyChangedFor(nameof(IsSigningCardVisible))]
    [NotifyPropertyChangedFor(nameof(IsKnownGoodApkVisible))]
    private string _publishPlatform = PublisherService.AndroidPlatform;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReloadProjectMetadataCommand))]
    [NotifyPropertyChangedFor(nameof(HasProjectSelection))]
    private string _projectDirectory = string.Empty;

    [ObservableProperty]
    private string _selectedProjectFile = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private string _targetFramework = "net10.0-android";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private string _runtimeIdentifier = "android-arm64";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private string _configuration = "Release";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenPublishFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallLatestApkCommand))]
    private string _outputDirectory = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UninstallAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(LaunchAppCommand))]
    private string _packageId = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private bool _includeApk = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private bool _includeAab;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private bool _selfContained = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private bool _publishTrimmed;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private bool _publishAot;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private bool _publishReadyToRun;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private bool _publishSingleFile = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private bool _useAppHost = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private bool _createMacAppBundle = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private bool _createWindowsExecutable = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private bool _buildIpa;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private bool _archiveOnBuild = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private bool _runAotCompilation;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private bool _enableProfiledAot;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    [NotifyPropertyChangedFor(nameof(IsShrinkerSettingsEnabled))]
    private string _androidLinkMode = "None";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private string _androidLinkTool = "r8";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private string _androidDexTool = "d8";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private bool _createMappingFile;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private bool _enableMultiDex;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private bool _useAapt2 = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private bool _enableDesugar = true;

    [ObservableProperty]
    private bool _deleteBin = true;

    [ObservableProperty]
    private bool _deleteObj = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    [NotifyPropertyChangedFor(nameof(IsSigningEnabled))]
    [NotifyPropertyChangedFor(nameof(IsSigningCardVisible))]
    private string _signMode = "Auto";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private string _keystorePath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private string _keyAlias = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private string _keystorePassword = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private string _keyPassword = string.Empty;

    [ObservableProperty]
    private string _commandPreview = "Select a project to generate a publish command.";

    [ObservableProperty]
    private string _knownGoodApkCommand = string.Empty;

    [ObservableProperty]
    private string _liveOutput = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _dotnetStatus = string.Empty;

    [ObservableProperty]
    private string _adbStatus = string.Empty;

    [ObservableProperty]
    private string _emulatorStatus = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LaunchEmulatorCommand))]
    private string _selectedEmulator = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSharedProgressVisible))]
    [NotifyPropertyChangedFor(nameof(IsSharedProgressIndeterminate))]
    [NotifyPropertyChangedFor(nameof(SharedProgressText))]
    [NotifyPropertyChangedFor(nameof(SharedProgressValue))]
    private bool _isScreenshotCountdownVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SharedProgressText))]
    private string _screenshotCountdownText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SharedProgressValue))]
    private double _screenshotCountdownProgress;

    public ObservableCollection<string> AvailableEmulators { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReloadProjectMetadataCommand))]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenPublishFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallLatestApkCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(LaunchAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteDesktopAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyWindowScreenshotCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveWindowScreenshotToDiskCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyCommandPreviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyKnownGoodApkCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyLiveOutputCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshEmulatorsCommand))]
    [NotifyCanExecuteChangedFor(nameof(LaunchEmulatorCommand))]
    [NotifyPropertyChangedFor(nameof(IsEditorEnabled))]
    [NotifyPropertyChangedFor(nameof(IsSharedProgressVisible))]
    [NotifyPropertyChangedFor(nameof(IsSharedProgressIndeterminate))]
    [NotifyPropertyChangedFor(nameof(SharedProgressText))]
    [NotifyPropertyChangedFor(nameof(SharedProgressValue))]
    private bool _isBusy;

    public bool IsEditorEnabled => !IsBusy;

    public bool IsSharedProgressVisible => IsBusy;

    public bool IsSharedProgressIndeterminate => IsBusy && !IsScreenshotCountdownVisible;

    public string SharedProgressText => IsScreenshotCountdownVisible ? ScreenshotCountdownText : string.Empty;

    public double SharedProgressValue => IsScreenshotCountdownVisible ? ScreenshotCountdownProgress : 0;

    public bool HasProjectSelection => !string.IsNullOrWhiteSpace(ProjectDirectory);

    public bool IsAndroidPlatform => string.Equals(PublishPlatform, PublisherService.AndroidPlatform, StringComparison.Ordinal);

    public bool IsMacOsPlatform => string.Equals(PublishPlatform, PublisherService.MacOsPlatform, StringComparison.Ordinal);

    public bool IsAndroidActionsVisible => IsAndroidPlatform;

    public bool IsDesktopActionsVisible => IsMacOsPlatform;

    public bool IsAndroidPublishOptionsVisible => IsAndroidPlatform;

    public bool IsMacOsPublishOptionsVisible => IsMacOsPlatform;

    public bool IsWindowsPublishOptionsVisible => string.Equals(PublishPlatform, PublisherService.WindowsPlatform, StringComparison.Ordinal);

    public bool IsIosPublishOptionsVisible => string.Equals(PublishPlatform, PublisherService.IosPlatform, StringComparison.Ordinal);

    public bool IsSigningEnabled => string.Equals(SignMode, "Sign", StringComparison.Ordinal);

    public bool IsSigningCardVisible => IsAndroidPlatform && IsSigningEnabled;

    public bool IsKnownGoodApkVisible => IsAndroidPlatform;

    public bool IsShrinkerSettingsEnabled => !string.Equals(AndroidLinkMode, "None", StringComparison.Ordinal);

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName is not null && PreviewSensitiveProperties.Contains(e.PropertyName))
        {
            RefreshCommandPreview();
        }
    }

    partial void OnPublishPlatformChanged(string value)
    {
        if (string.Equals(value, PublisherService.AndroidPlatform, StringComparison.Ordinal))
        {
            if (!TargetFramework.Contains("android", StringComparison.OrdinalIgnoreCase))
            {
                TargetFramework = "net10.0-android";
            }

            if (!RuntimeIdentifier.Contains("android", StringComparison.OrdinalIgnoreCase))
            {
                RuntimeIdentifier = "android-arm64";
            }
        }
        else
        {
            if (string.Equals(value, PublisherService.MacOsPlatform, StringComparison.Ordinal))
            {
                if (TargetFramework.Contains("android", StringComparison.OrdinalIgnoreCase))
                {
                    TargetFramework = "net10.0";
                }

                if (RuntimeIdentifier.Contains("android", StringComparison.OrdinalIgnoreCase))
                {
                    RuntimeIdentifier = "osx-arm64";
                }
            }
            else if (string.Equals(value, PublisherService.WindowsPlatform, StringComparison.Ordinal))
            {
                TargetFramework = "net10.0-windows";
                RuntimeIdentifier = "win-x64";
            }
            else if (string.Equals(value, PublisherService.IosPlatform, StringComparison.Ordinal))
            {
                TargetFramework = "net10.0-ios";
                RuntimeIdentifier = "ios-arm64";
            }
        }

        OutputDirectory = string.Empty;

        if (!string.IsNullOrWhiteSpace(ProjectDirectory))
        {
            _ = ReloadProjectMetadataAsync();
        }
    }

    [RelayCommand]
    private async Task BrowseProjectDirectoryAsync()
    {
        var selected = await _desktopInteractionService.PickFolderAsync("Select .NET project directory", ProjectDirectory);
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        ProjectDirectory = selected;
        await ReloadProjectMetadataAsync();
    }

    [RelayCommand]
    private async Task BrowseOutputDirectoryAsync()
    {
        var selected = await _desktopInteractionService.PickFolderAsync("Select publish output directory", OutputDirectory);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            OutputDirectory = selected;
        }
    }

    [RelayCommand]
    private async Task BrowseKeystoreAsync()
    {
        var selected = await _desktopInteractionService.PickFileAsync("Select signing keystore", KeystorePath);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            KeystorePath = selected;
        }
    }

    [RelayCommand(CanExecute = nameof(CanReloadProjectMetadata))]
    private async Task ReloadProjectMetadataAsync()
    {
        try
        {
            var metadata = _publisherService.LoadProjectMetadata(ProjectDirectory, Configuration, TargetFramework, RuntimeIdentifier, PublishPlatform);
            SelectedProjectFile = metadata.ProjectFilePath;

            if (string.IsNullOrWhiteSpace(OutputDirectory))
            {
                OutputDirectory = metadata.DefaultOutputDirectory;
            }

            if (!string.IsNullOrWhiteSpace(metadata.PackageId))
            {
                PackageId = metadata.PackageId;
            }

            if (!string.IsNullOrWhiteSpace(metadata.TargetFramework))
            {
                TargetFramework = metadata.TargetFramework;
            }

            if (IsAndroidPlatform && !RuntimeIdentifier.Contains("android", StringComparison.OrdinalIgnoreCase))
            {
                RuntimeIdentifier = "android-arm64";
            }

            if (IsMacOsPlatform)
            {
                RuntimeIdentifier = TargetFramework.Contains("maccatalyst", StringComparison.OrdinalIgnoreCase)
                    ? "maccatalyst-arm64"
                    : "osx-arm64";
            }
            else if (string.Equals(PublishPlatform, PublisherService.WindowsPlatform, StringComparison.Ordinal))
            {
                if (!RuntimeIdentifier.StartsWith("win-", StringComparison.OrdinalIgnoreCase))
                {
                    RuntimeIdentifier = "win-x64";
                }
            }
            else if (string.Equals(PublishPlatform, PublisherService.IosPlatform, StringComparison.Ordinal))
            {
                if (!RuntimeIdentifier.StartsWith("ios-", StringComparison.OrdinalIgnoreCase))
                {
                    RuntimeIdentifier = "ios-arm64";
                }
            }

            StatusMessage = "Project metadata loaded. Review the command preview before publishing.";
        }
        catch (Exception ex)
        {
            SelectedProjectFile = string.Empty;
            StatusMessage = "Could not load project metadata.";
            await _desktopInteractionService.ShowErrorAsync("Project metadata", ex.Message);
        }
    }

    private bool CanReloadProjectMetadata()
    {
        return !IsBusy && !string.IsNullOrWhiteSpace(ProjectDirectory);
    }

    [RelayCommand(CanExecute = nameof(CanPublish))]
    private async Task PublishAsync()
    {
        try
        {
            IsBusy = true;
            ClearLog();
            AppendLog("Preparing publish workflow..." + Environment.NewLine);
            var success = await _publisherService.PublishAsync(CreateConfiguration(), text => Dispatcher.UIThread.Post(() => AppendLog(text)), CancellationToken.None);
            StatusMessage = success
                ? "Publish completed successfully."
                : "Publish failed. Check the live output for the exact toolchain error.";
            RefreshCommandPreview();
        }
        catch (Exception ex)
        {
            StatusMessage = "Publish could not start.";
            await _desktopInteractionService.ShowErrorAsync("Publish failed", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanPublish()
    {
        return !IsBusy;
    }

    [RelayCommand(CanExecute = nameof(CanOpenPublishFolder))]
    private async Task OpenPublishFolderAsync()
    {
        try
        {
            _publisherService.OpenPublishFolder(OutputDirectory);
        }
        catch (Exception ex)
        {
            await _desktopInteractionService.ShowErrorAsync("Open publish folder", ex.Message);
        }
    }

    private bool CanOpenPublishFolder()
    {
        return !IsBusy && !string.IsNullOrWhiteSpace(OutputDirectory);
    }

    [RelayCommand(CanExecute = nameof(CanInstallLatestApk))]
    private async Task InstallLatestApkAsync()
    {
        await RunQuickActionAsync(
            async () => await _publisherService.InstallLatestApkAsync(OutputDirectory, CancellationToken.None),
            "Install APK");
    }

    private bool CanInstallLatestApk()
    {
        return IsAndroidPlatform && !IsBusy && !string.IsNullOrWhiteSpace(OutputDirectory);
    }

    [RelayCommand(CanExecute = nameof(CanUsePackageIdActions))]
    private async Task UninstallAppAsync()
    {
        await RunQuickActionAsync(
            async () => await _publisherService.UninstallAsync(PackageId, CancellationToken.None),
            "Uninstall app");
    }

    [RelayCommand(CanExecute = nameof(CanUsePackageIdActions))]
    private async Task LaunchAppAsync()
    {
        await RunQuickActionAsync(
            async () => await _publisherService.LaunchAsync(PackageId, CancellationToken.None),
            "Launch app");
    }

    private bool CanUsePackageIdActions()
    {
        return IsAndroidPlatform && !IsBusy && !string.IsNullOrWhiteSpace(PackageId);
    }

    [RelayCommand(CanExecute = nameof(CanDeleteDesktopApp))]
    private async Task DeleteDesktopAppAsync()
    {
        await RunQuickActionAsync(
            async () => await _publisherService.DeletePublishedDesktopAppAsync(OutputDirectory, CancellationToken.None),
            "Delete desktop app");
    }

    private bool CanDeleteDesktopApp()
    {
        return IsMacOsPlatform && !IsBusy && !string.IsNullOrWhiteSpace(OutputDirectory);
    }

    [RelayCommand(CanExecute = nameof(CanCopyWindowScreenshot))]
    private async Task CopyWindowScreenshotAsync()
    {
        await RunQuickActionAsync(
            async () =>
            {
                await RunScreenshotCountdownAsync();
                return await _publisherService.CopyWindowScreenshotToClipboardAsync(_desktopInteractionService.Window, CancellationToken.None);
            },
            "Screenshot");
    }

    [RelayCommand(CanExecute = nameof(CanCopyWindowScreenshot))]
    private async Task SaveWindowScreenshotToDiskAsync()
    {
        await RunQuickActionAsync(
            async () =>
            {
                await RunScreenshotCountdownAsync();
                return await _publisherService.SaveWindowScreenshotToDiskAsync(_desktopInteractionService.Window, OutputDirectory, CancellationToken.None);
            },
            "Screenshot to disk");
    }

    [RelayCommand(CanExecute = nameof(CanRefreshEmulators))]
    private async Task RefreshEmulatorsAsync()
    {
        try
        {
            IsBusy = true;
            var emulators = await _publisherService.DiscoverEmulatorsAsync(CancellationToken.None);
            AvailableEmulators.Clear();
            foreach (var emulator in emulators)
            {
                AvailableEmulators.Add(emulator);
            }

            if (AvailableEmulators.Count == 0)
            {
                SelectedEmulator = string.Empty;
                StatusMessage = "No Android emulators discovered.";
            }
            else if (string.IsNullOrWhiteSpace(SelectedEmulator) || !AvailableEmulators.Contains(SelectedEmulator))
            {
                SelectedEmulator = AvailableEmulators[0];
                StatusMessage = $"Discovered {AvailableEmulators.Count} emulator(s).";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = "Emulator discovery failed.";
            await _desktopInteractionService.ShowErrorAsync("Discover emulators", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanLaunchEmulator))]
    private async Task LaunchEmulatorAsync()
    {
        await RunQuickActionAsync(
            async () => await _publisherService.LaunchEmulatorAsync(SelectedEmulator, CancellationToken.None),
            "Launch emulator");
    }

    private bool CanCopyWindowScreenshot()
    {
        return !IsBusy;
    }

    [RelayCommand(CanExecute = nameof(CanCopyCommandPreview))]
    private async Task CopyCommandPreviewAsync()
    {
        await _desktopInteractionService.CopyTextToClipboardAsync(CommandPreview);
        StatusMessage = "Command preview copied.";
    }

    private bool CanCopyCommandPreview()
    {
        return !IsBusy && !string.IsNullOrWhiteSpace(CommandPreview);
    }

    [RelayCommand(CanExecute = nameof(CanCopyKnownGoodApk))]
    private async Task CopyKnownGoodApkAsync()
    {
        await _desktopInteractionService.CopyTextToClipboardAsync(KnownGoodApkCommand);
        StatusMessage = "Known-good APK command copied.";
    }

    private bool CanCopyKnownGoodApk()
    {
        return IsAndroidPlatform && !IsBusy && !string.IsNullOrWhiteSpace(KnownGoodApkCommand);
    }

    [RelayCommand(CanExecute = nameof(CanCopyLiveOutput))]
    private async Task CopyLiveOutputAsync()
    {
        await _desktopInteractionService.CopyTextToClipboardAsync(LiveOutput);
        StatusMessage = "Live output copied.";
    }

    private bool CanCopyLiveOutput()
    {
        return !IsBusy && !string.IsNullOrWhiteSpace(LiveOutput);
    }

    private bool CanRefreshEmulators()
    {
        return IsAndroidPlatform && !IsBusy;
    }

    private bool CanLaunchEmulator()
    {
        return IsAndroidPlatform && !IsBusy && !string.IsNullOrWhiteSpace(SelectedEmulator);
    }

    private async Task RunQuickActionAsync(Func<Task<string>> action, string actionName)
    {
        try
        {
            IsBusy = true;
            var message = await action();
            AppendLog(Environment.NewLine + $"[{actionName}] {message}" + Environment.NewLine);
            StatusMessage = $"{actionName} finished.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"{actionName} failed.";
            await _desktopInteractionService.ShowErrorAsync(actionName, ex.Message);
        }
        finally
        {
            ResetScreenshotCountdown();
            IsBusy = false;
        }
    }

    private async Task RunScreenshotCountdownAsync()
    {
        IsScreenshotCountdownVisible = true;
        ScreenshotCountdownProgress = 100;

        for (var secondsRemaining = 5; secondsRemaining >= 1; secondsRemaining--)
        {
            ScreenshotCountdownText = $"Screenshot in {secondsRemaining}s";
            ScreenshotCountdownProgress = secondsRemaining * 20;
            StatusMessage = $"Preparing screenshot. Capturing in {secondsRemaining} second(s).";
            await Task.Delay(1000);
        }

        ScreenshotCountdownText = "Capturing screenshot...";
        ScreenshotCountdownProgress = 0;
    }

    private void ResetScreenshotCountdown()
    {
        IsScreenshotCountdownVisible = false;
        ScreenshotCountdownText = string.Empty;
        ScreenshotCountdownProgress = 0;
    }

    private PublishConfiguration CreateConfiguration()
    {
        return new PublishConfiguration
        {
            PublishPlatform = PublishPlatform,
            ProjectDirectory = ProjectDirectory,
            TargetFramework = TargetFramework,
            RuntimeIdentifier = RuntimeIdentifier,
            Configuration = Configuration,
            OutputDirectory = OutputDirectory,
            PackageId = PackageId,
            IncludeApk = IncludeApk,
            IncludeAab = IncludeAab,
            SelfContained = SelfContained,
            PublishTrimmed = PublishTrimmed,
            PublishAot = PublishAot,
            PublishReadyToRun = PublishReadyToRun,
            PublishSingleFile = PublishSingleFile,
            UseAppHost = UseAppHost,
            CreateMacAppBundle = CreateMacAppBundle,
            CreateWindowsExecutable = CreateWindowsExecutable,
            BuildIpa = BuildIpa,
            ArchiveOnBuild = ArchiveOnBuild,
            RunAotCompilation = RunAotCompilation,
            EnableProfiledAot = EnableProfiledAot,
            AndroidLinkMode = AndroidLinkMode,
            AndroidLinkTool = AndroidLinkTool,
            AndroidDexTool = AndroidDexTool,
            CreateMappingFile = CreateMappingFile,
            EnableMultiDex = EnableMultiDex,
            UseAapt2 = UseAapt2,
            EnableDesugar = EnableDesugar,
            DeleteBin = DeleteBin,
            DeleteObj = DeleteObj,
            SignMode = SignMode,
            KeystorePath = KeystorePath,
            KeyAlias = KeyAlias,
            KeystorePassword = KeystorePassword,
            KeyPassword = KeyPassword
        };
    }

    private void RefreshCommandPreview()
    {
        try
        {
            var bundle = _publisherService.BuildPublishCommandBundle(CreateConfiguration());
            SelectedProjectFile = bundle.ProjectFilePath;
            if (string.IsNullOrWhiteSpace(OutputDirectory))
            {
                OutputDirectory = bundle.OutputDirectory;
            }

            CommandPreview = bundle.PreviewText;
            KnownGoodApkCommand = bundle.VerifiedApkPreviewText;
        }
        catch (Exception ex)
        {
            CommandPreview = $"Invalid config: {ex.Message}";
            KnownGoodApkCommand = string.Empty;
        }
    }

    private void AppendLog(string text)
    {
        _logBuilder.Append(text);
        LiveOutput = _logBuilder.ToString();
    }

    private void ClearLog()
    {
        _logBuilder.Clear();
        LiveOutput = string.Empty;
    }
}
