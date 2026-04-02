using System.ComponentModel;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MauiAppPublisher.Models;
using MauiAppPublisher.Services;

namespace MauiAppPublisher.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private static readonly HashSet<string> PreviewSensitiveProperties =
    [
        nameof(ProjectDirectory),
        nameof(TargetFramework),
        nameof(RuntimeIdentifier),
        nameof(Configuration),
        nameof(OutputDirectory),
        nameof(IncludeApk),
        nameof(IncludeAab),
        nameof(SelfContained),
        nameof(PublishTrimmed),
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
        StatusMessage = "Select a MAUI project folder to start building your publish command.";
        RefreshCommandPreview();
        _ = RefreshEmulatorsAsync();
    }

    public IReadOnlyList<string> ConfigurationOptions { get; } = ["Release", "Debug"];

    public IReadOnlyList<string> LinkModeOptions { get; } = ["None", "SdkOnly", "Full"];

    public IReadOnlyList<string> LinkToolOptions { get; } = ["r8", "proguard"];

    public IReadOnlyList<string> DexToolOptions { get; } = ["d8", "dx"];

    public IReadOnlyList<string> SignModeOptions { get; } = ["Auto", "Sign", "Do Not Sign"];

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
    [NotifyCanExecuteChangedFor(nameof(CopyWindowScreenshotCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveWindowScreenshotToDiskCommand))]
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

    public bool IsSigningEnabled => string.Equals(SignMode, "Sign", StringComparison.Ordinal);

    public bool IsShrinkerSettingsEnabled => !string.Equals(AndroidLinkMode, "None", StringComparison.Ordinal);

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName is not null && PreviewSensitiveProperties.Contains(e.PropertyName))
        {
            RefreshCommandPreview();
        }
    }

    [RelayCommand]
    private async Task BrowseProjectDirectoryAsync()
    {
        var selected = await _desktopInteractionService.PickFolderAsync("Select MAUI project directory", ProjectDirectory);
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
            var metadata = _publisherService.LoadProjectMetadata(ProjectDirectory, Configuration, TargetFramework, RuntimeIdentifier);
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
        return !IsBusy && !string.IsNullOrWhiteSpace(OutputDirectory);
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
        return !IsBusy && !string.IsNullOrWhiteSpace(PackageId);
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

    private bool CanRefreshEmulators()
    {
        return !IsBusy;
    }

    private bool CanLaunchEmulator()
    {
        return !IsBusy && !string.IsNullOrWhiteSpace(SelectedEmulator);
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
