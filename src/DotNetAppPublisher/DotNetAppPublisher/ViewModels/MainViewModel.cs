using System.Reflection;
using System.ComponentModel;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotNetAppPublisher.Models;
using DotNetAppPublisher.Services;
using DotNetAppPublisher.Views;

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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallLatestApkCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(LaunchAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyFileToAndroidDeviceCommand))]
    private AndroidDeviceInfo? _selectedDevice;

    public MainViewModel(PublisherService publisherService, DesktopInteractionService desktopInteractionService)
    {
        _publisherService = publisherService;
        _desktopInteractionService = desktopInteractionService;
        IsProjectInternalVersionSupported = GetDefaultInternalVersionSupport(PublishPlatform);
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

    public IReadOnlyList<string> ThemeOptions { get; } = ["System", "Light", "Dark"];

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
    [NotifyPropertyChangedFor(nameof(ProjectInternalVersionLabel))]
    [NotifyPropertyChangedFor(nameof(IsAppVersionVisible))]
    private string _publishPlatform = PublisherService.AndroidPlatform;

[ObservableProperty]
[NotifyCanExecuteChangedFor(nameof(ReloadProjectMetadataCommand))]
[NotifyCanExecuteChangedFor(nameof(InstallLatestApkCommand))]
[NotifyPropertyChangedFor(nameof(HasProjectSelection))]
[NotifyPropertyChangedFor(nameof(IsPackageIdVisible))]
private string _projectDirectory = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProjectVersion))]
    private string _projectDisplayVersion = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AppVersion))]
    private string _projectInternalVersion = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAppVersionVisible))]
    private bool _isProjectInternalVersionSupported;

    [ObservableProperty]
    private string _selectedProjectFile = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private string _targetFramework = "net10.0-android";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    [NotifyPropertyChangedFor(nameof(IsIosDeviceRuntime))]
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
    private string _theme = "System";

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

    [ObservableProperty]
    private bool _isScreenshotDialogVisible;

    [ObservableProperty]
    private string _screenshotDialogMessage = string.Empty;

    public ObservableCollection<string> AvailableEmulators { get; } = [];
    public ObservableCollection<AndroidDeviceInfo> AvailableAndroidDevices { get; } = [];
    public ObservableCollection<string> AvailableIosSimulators { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallIosAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallIosAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(LaunchIosAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyFileToIosDeviceCommand))]
    private string _selectedIosSimulator = string.Empty;

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
    [NotifyCanExecuteChangedFor(nameof(RefreshDevicesCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshIosSimulatorsCommand))]
    [NotifyCanExecuteChangedFor(nameof(LaunchDeviceCommand))]
    [NotifyCanExecuteChangedFor(nameof(LaunchIosSimulatorCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallIosAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallIosAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(LaunchIosAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyFileToAndroidDeviceCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyFileToIosDeviceCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelPublishCommand))]
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

    public bool IsIosPlatform => string.Equals(PublishPlatform, PublisherService.IosPlatform, StringComparison.Ordinal);

    public bool IsAndroidActionsVisible => IsAndroidPlatform;

    public bool IsDesktopActionsVisible => IsMacOsPlatform;

    public bool IsAndroidPublishOptionsVisible => IsAndroidPlatform;

    public bool IsMacOsPublishOptionsVisible => IsMacOsPlatform;

    public bool IsWindowsPublishOptionsVisible => string.Equals(PublishPlatform, PublisherService.WindowsPlatform, StringComparison.Ordinal);

    public bool IsIosPublishOptionsVisible => string.Equals(PublishPlatform, PublisherService.IosPlatform, StringComparison.Ordinal);

    public bool IsIosActionsVisible => IsIosPublishOptionsVisible;

    public bool IsPackageIdVisible => !string.IsNullOrWhiteSpace(PackageId);

    public bool IsIosDeviceRuntime => string.Equals(PublishPlatform, PublisherService.IosPlatform, StringComparison.Ordinal);

    public bool IsSigningEnabled => string.Equals(SignMode, "Sign", StringComparison.Ordinal);

    public bool IsSigningCardVisible => IsAndroidPlatform && IsSigningEnabled;

    public bool IsKnownGoodApkVisible => IsAndroidPlatform;

    public bool IsShrinkerSettingsEnabled => !string.Equals(AndroidLinkMode, "None", StringComparison.Ordinal);

    public string PackageIdLabel => "Package ID";

    public string PackageIdDescription => "The unique identifier for your application.";

    public string ProjectVersion => ProjectDisplayVersion;

    public string AppVersion => ProjectInternalVersion;

    public bool IsAppVersionVisible => IsProjectInternalVersionSupported;

    public string ProjectInternalVersionLabel => PublishPlatform switch
    {
        PublisherService.AndroidPlatform => "Version Code",
        PublisherService.IosPlatform => "Build Number",
        PublisherService.MacOsPlatform => "Build Number",
        _ => "Internal Version"
    };

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
        IsProjectInternalVersionSupported = GetDefaultInternalVersionSupport(value);

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

    partial void OnThemeChanged(string value)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant = value switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
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

    [RelayCommand]
    private async Task RefreshDevicesAsync()
    {
        try
        {
            IsBusy = true;
            var devices = await _publisherService.DiscoverAndroidDevicesAsync(CancellationToken.None);
            var previousSelection = SelectedDevice;
            AvailableAndroidDevices.Clear();
            foreach (var device in devices)
            {
                AvailableAndroidDevices.Add(device);
            }

            if (previousSelection is not null)
            {
                SelectedDevice = AvailableAndroidDevices.FirstOrDefault(candidate => IsSameAndroidTarget(candidate, previousSelection));
            }

            if (AvailableAndroidDevices.Count == 0)
            {
                SelectedDevice = null;
                StatusMessage = "No Android devices discovered. Connect a device or launch an emulator, then refresh.";
            }
            else if (SelectedDevice is null)
            {
                StatusMessage = $"Discovered {AvailableAndroidDevices.Count} Android target(s). Select a device to use platform actions.";
            }
            else
            {
                StatusMessage = $"Discovered {AvailableAndroidDevices.Count} Android target(s).";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = "Android device discovery failed.";
            await _desktopInteractionService.ShowErrorAsync("Discover devices", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshIosSimulatorsAsync()
    {
        try
        {
            IsBusy = true;
            var simulators = await _publisherService.DiscoverIosSimulatorsAsync(CancellationToken.None);
            var previousSelection = SelectedIosSimulator;
            AvailableIosSimulators.Clear();
            foreach (var simulator in simulators)
            {
                AvailableIosSimulators.Add(simulator);
            }

            if (!string.IsNullOrWhiteSpace(previousSelection) && AvailableIosSimulators.Contains(previousSelection))
            {
                SelectedIosSimulator = previousSelection;
            }
            else
            {
                SelectedIosSimulator = string.Empty;
            }

            if (AvailableIosSimulators.Count == 0)
            {
                StatusMessage = "No iOS simulators discovered. Install a simulator runtime, then refresh.";
            }
            else if (string.IsNullOrWhiteSpace(SelectedIosSimulator))
            {
                StatusMessage = $"Discovered {AvailableIosSimulators.Count} iOS simulator(s). Select one to use platform actions.";
            }
            else
            {
                StatusMessage = $"Discovered {AvailableIosSimulators.Count} iOS simulator(s).";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = "iOS simulator discovery failed.";
            await _desktopInteractionService.ShowErrorAsync("Discover simulators", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LaunchDeviceAsync()
    {
        if (SelectedDevice is null)
        {
            StatusMessage = "Select an Android device first.";
            return;
        }

        if (SelectedDevice.IsRunning)
        {
            await RunQuickActionAsync(
                async () => await _publisherService.LaunchAsync(PackageId, SelectedDevice.Serial, CancellationToken.None),
                "Launch app");
        }
        else
        {
            await RunQuickActionAsync(
                async () => await _publisherService.LaunchEmulatorAsync(SelectedDevice.Name, CancellationToken.None),
                "Launch emulator");
        }
    }

    [RelayCommand]
    private async Task LaunchIosSimulatorAsync()
    {
        await RunQuickActionAsync(
            async () => await _publisherService.LaunchIosSimulatorAsync(SelectedIosSimulator, CancellationToken.None),
            "Launch simulator");
    }

    [RelayCommand(CanExecute = nameof(CanInstallIosApp))]
    private async Task InstallIosAppAsync()
    {
        await RunQuickActionAsync(
            async () => await _publisherService.InstallIosAppAsync(OutputDirectory, SelectedIosSimulator, CancellationToken.None),
            "Install iOS app");
    }

    private bool CanInstallIosApp()
    {
        return IsIosPlatform && !IsBusy && !string.IsNullOrWhiteSpace(OutputDirectory) && !string.IsNullOrWhiteSpace(SelectedIosSimulator);
    }

    [RelayCommand(CanExecute = nameof(CanUseIosPackageActions))]
    private async Task UninstallIosAppAsync()
    {
        await RunQuickActionAsync(
            async () => await _publisherService.UninstallIosAppAsync(PackageId, SelectedIosSimulator, CancellationToken.None),
            "Uninstall iOS app");
    }

    [RelayCommand(CanExecute = nameof(CanUseIosPackageActions))]
    private async Task LaunchIosAppAsync()
    {
        await RunQuickActionAsync(
            async () => await _publisherService.LaunchIosAppAsync(PackageId, SelectedIosSimulator, CancellationToken.None),
            "Launch iOS app");
    }

    private bool CanUseIosPackageActions()
    {
        return IsIosPlatform && !IsBusy && !string.IsNullOrWhiteSpace(PackageId) && !string.IsNullOrWhiteSpace(SelectedIosSimulator);
    }

    [RelayCommand(CanExecute = nameof(CanCopyFileToAndroidDevice))]
    private async Task CopyFileToAndroidDeviceAsync()
    {
        if (!TryValidateAndroidDeviceSelection("Copy file"))
        {
            return;
        }

        var filePath = await _desktopInteractionService.PickFileAsync("Select file to copy to device");
        if (string.IsNullOrWhiteSpace(filePath))
        {
            StatusMessage = "Copy canceled.";
            return;
        }

        await RunQuickActionAsync(
            async () => await _publisherService.PushFileToDownloadsAsync(filePath, SelectedDevice?.Serial, CancellationToken.None),
            "Push file");
    }

    private bool CanCopyFileToAndroidDevice()
    {
        return IsAndroidPlatform && !IsBusy;
    }

    [RelayCommand(CanExecute = nameof(CanCopyFileToIosDevice))]
    private async Task CopyFileToIosDeviceAsync()
    {
        if (!TryValidateIosSimulatorSelection("Copy file"))
        {
            return;
        }

        var filePath = await _desktopInteractionService.PickFileAsync("Select file to copy to device");
        if (string.IsNullOrWhiteSpace(filePath))
        {
            StatusMessage = "Copy canceled.";
            return;
        }

        await RunQuickActionAsync(
            async () => await _publisherService.PushFileToSimulatorAsync(filePath, SelectedIosSimulator, CancellationToken.None),
            "Push file");
    }

    private bool CanCopyFileToIosDevice()
    {
        return IsIosPlatform && !IsBusy;
    }

    [RelayCommand]
    private async Task CancelPublishAsync()
    {
        StatusMessage = "Publish cancelled.";
    }

    public bool IsPublishing => IsBusy;

    public bool CanCancelPublish => IsBusy;

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

            ProjectDisplayVersion = metadata.DisplayVersion ?? string.Empty;
            ProjectInternalVersion = metadata.InternalVersion ?? string.Empty;
            IsProjectInternalVersionSupported = metadata.SupportsInternalVersion;

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

    private static bool GetDefaultInternalVersionSupport(string publishPlatform)
    {
        return string.Equals(publishPlatform, PublisherService.AndroidPlatform, StringComparison.Ordinal)
            || string.Equals(publishPlatform, PublisherService.IosPlatform, StringComparison.Ordinal)
            || string.Equals(publishPlatform, PublisherService.MacOsPlatform, StringComparison.Ordinal);
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

    [RelayCommand(CanExecute = nameof(CanRunAndroidActions))]
    private async Task InstallLatestApkAsync()
    {
        if (!TryValidateAndroidDeviceSelection("Install APK"))
        {
            return;
        }

        if (!TryValidateProjectSelection("Install APK"))
        {
            return;
        }

        if (!TryValidateOutputDirectory("Install APK"))
        {
            return;
        }

        if (!TryValidatePublishedApkExists())
        {
            return;
        }

        await RunQuickActionAsync(
            async () => await _publisherService.InstallLatestApkAsync(CreateConfiguration(), SelectedDevice?.Serial, CancellationToken.None),
            "Install APK");
    }

    private bool CanInstallLatestApk()
    {
        return CanRunAndroidActions();
    }

    [RelayCommand(CanExecute = nameof(CanRunAndroidActions))]
    private async Task UninstallAppAsync()
    {
        if (!TryValidateAndroidDeviceSelection("Uninstall app"))
        {
            return;
        }

        if (!TryValidateProjectSelection("Uninstall app"))
        {
            return;
        }

        if (!TryValidatePackageId("Uninstall app"))
        {
            return;
        }

        await RunQuickActionAsync(
            async () => await _publisherService.UninstallAsync(PackageId, SelectedDevice?.Serial, CancellationToken.None),
            "Uninstall app");
    }

    [RelayCommand(CanExecute = nameof(CanRunAndroidActions))]
    private async Task LaunchAppAsync()
    {
        if (!TryValidateAndroidDeviceSelection("Launch app"))
        {
            return;
        }

        if (!TryValidateProjectSelection("Launch app"))
        {
            return;
        }

        if (!TryValidatePackageId("Launch app"))
        {
            return;
        }

        await RunQuickActionAsync(
            async () => await _publisherService.LaunchAsync(PackageId, SelectedDevice?.Serial, CancellationToken.None),
            "Launch app");
    }

    private bool CanUsePackageIdActions()
    {
        return CanRunAndroidActions();
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
        await RunScreenshotActionAsync(
            async () => await _publisherService.CopyWindowScreenshotToClipboardAsync(_desktopInteractionService.Window, CancellationToken.None),
            "Screenshot to clipboard");
    }

    [RelayCommand(CanExecute = nameof(CanCopyWindowScreenshot))]
    private async Task SaveWindowScreenshotToDiskAsync()
    {
        await RunScreenshotActionAsync(
            async () => await _publisherService.SaveWindowScreenshotToDiskAsync(_desktopInteractionService.Window, OutputDirectory, CancellationToken.None),
            "Screenshot to disk");
    }

    private async Task RunScreenshotActionAsync(Func<Task<string>> action, string actionName)
    {
        try
        {
            IsBusy = true;
            
            // Create and show the screenshot dialog
            var dialog = new ScreenshotDialog();
            
            // Run the countdown and screenshot capture
            var message = await dialog.ShowCountdownAndCaptureAsync(5, async () =>
            {
                return await action();
            });
            
            // Update status
            StatusMessage = IsFailureMessage(message)
                ? $"{actionName} failed. Check Live Output for details."
                : $"✓ {actionName} complete!";
            
            AppendLog(Environment.NewLine + $"[{actionName}] {message}" + Environment.NewLine);
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
            StatusMessage = IsFailureMessage(message)
                ? $"{actionName} failed. Check Live Output for details."
                : $"{actionName} finished.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"{actionName} failed.";
            await _desktopInteractionService.ShowErrorAsync(actionName, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ResetScreenshotCountdown()
    {
        IsScreenshotCountdownVisible = false;
        ScreenshotCountdownText = string.Empty;
        ScreenshotCountdownProgress = 0;
    }

    private bool CanRunAndroidActions()
    {
        return IsAndroidPlatform && !IsBusy;
    }

    private bool TryValidateProjectSelection(string actionName)
    {
        if (HasProjectSelection)
        {
            return true;
        }

        ReportPreflightFailure(actionName, "Select a project first.");
        return false;
    }

    private bool TryValidateAndroidDeviceSelection(string actionName)
    {
        if (SelectedDevice is null)
        {
            ReportPreflightFailure(actionName, "Select an Android device first.");
            return false;
        }

        if (!SelectedDevice.IsRunning || string.IsNullOrWhiteSpace(SelectedDevice.Serial))
        {
            ReportPreflightFailure(actionName, "Select a running Android device first.");
            return false;
        }

        return true;
    }

    private bool TryValidateIosSimulatorSelection(string actionName)
    {
        if (!string.IsNullOrWhiteSpace(SelectedIosSimulator))
        {
            return true;
        }

        ReportPreflightFailure(actionName, "Select an iOS simulator first.");
        return false;
    }

    private bool TryValidatePackageId(string actionName)
    {
        if (!string.IsNullOrWhiteSpace(PackageId))
        {
            return true;
        }

        ReportPreflightFailure(actionName, "Package ID is required.");
        return false;
    }

    private bool TryValidateOutputDirectory(string actionName)
    {
        if (!string.IsNullOrWhiteSpace(OutputDirectory) && Directory.Exists(OutputDirectory))
        {
            return true;
        }

        ReportPreflightFailure(actionName, "Publish folder not found. Set Output Directory to a valid publish folder.");
        return false;
    }

    private bool TryValidatePublishedApkExists()
    {
        if (Directory.EnumerateFiles(OutputDirectory, "*.apk", SearchOption.AllDirectories).Any())
        {
            return true;
        }

        ReportPreflightFailure("Install APK", "No APK found in the publish folder. Publish first, then retry.");
        return false;
    }

    private void ReportPreflightFailure(string actionName, string reason)
    {
        var message = $"{actionName}: {reason}";
        StatusMessage = message;
        AppendLog(Environment.NewLine + $"[{actionName}] {reason}" + Environment.NewLine);
    }

    private static bool IsSameAndroidTarget(AndroidDeviceInfo left, AndroidDeviceInfo right)
    {
        return string.Equals(left.Serial, right.Serial, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Type, right.Type, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFailureMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("error", StringComparison.OrdinalIgnoreCase)
            || message.Contains("exception", StringComparison.OrdinalIgnoreCase);
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
