using System.Reflection;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CodePublisher.Features.Deployment.Android;
using CodePublisher.Features.GooglePlay;
using CodePublisher.Features.GooglePlay.Models;
using CodePublisher.Features.NuGet;
using CodePublisher.Features.GooglePlay.Tracks;
using CodePublisher.Features.Publishing.Artifacts;
using CodePublisher.Features.Publishing.Capture;
using CodePublisher.Features.Publishing.Configure;
using CodePublisher.Features.Publishing.Inspect;
using CodePublisher.Models;
using CodePublisher.Services;
using CodePublisher.Views;

namespace CodePublisher.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private static readonly HashSet<string> PreviewSensitiveProperties =
    [
        nameof(PublishPlatform),
        nameof(ProjectDirectory),
        nameof(SelectedProjectFile),
        nameof(TargetFramework),
        nameof(RuntimeIdentifier),
        nameof(Configuration),
        nameof(OutputDirectory),
        nameof(IncludeApk),
        nameof(IncludeAab),
        nameof(IsAndroidPublishOptionsVisible),
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
    private readonly object _logLock = new();
    private bool _logFlushScheduled;
    private long _lastLogFlushTicks;
    private const int LogFlushIntervalMs = 150;
    private readonly DesktopInteractionService _desktopInteractionService;
    private readonly PublisherService _publisherService;
    private readonly IScreenshotService _screenshotService;
    private readonly GooglePlayViewModel _googlePlayViewModel;
    private CancellationTokenSource? _publishCts;
    private ProjectOutputLayout? _projectOutputLayout;
    private bool _isAssigningAutomaticOutputDirectory;
    private bool _isOutputDirectoryManualOverride;
    private bool _isNormalizingPublishSettings;
    private bool _isReloadingMetadata;

    /// <summary>Every .csproj found under the selected folder, so a source root with several projects can be picked from.</summary>
    public ObservableCollection<string> AvailableProjectFiles { get; } = [];

    public bool HasMultipleProjectFiles => AvailableProjectFiles.Count > 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallLatestApkCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(LaunchAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyFileToAndroidDeviceCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshDeviceFilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(GoUpDeviceFilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(GoToDeviceLocationCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenDeviceFilesPathCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendFilesToDeviceCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnableWirelessDebuggingCommand))]
    [NotifyPropertyChangedFor(nameof(DeviceFilesHint))]
    [NotifyPropertyChangedFor(nameof(SelectedDeviceDetail))]
    [NotifyPropertyChangedFor(nameof(HasSelectedDeviceDetail))]
    private AndroidDeviceInfo? _selectedDevice;

    /// <summary>
    /// Detail row under the device selector: Android version and API level, CPU ABI, transport, and
    /// readiness, read from the device itself.
    /// </summary>
    public string SelectedDeviceDetail => SelectedDevice?.DetailSummary ?? string.Empty;

    public bool HasSelectedDeviceDetail => SelectedDevice?.HasDetail == true;

    public MainViewModel(
        PublisherService publisherService,
        DesktopInteractionService desktopInteractionService,
        IScreenshotService? screenshotService = null,
        GooglePlayViewModel? googlePlayViewModel = null,
        NuGetViewModel? nugetViewModel = null)
    {
        _publisherService = publisherService;
        _desktopInteractionService = desktopInteractionService;
        _screenshotService = screenshotService ?? new AvaloniaScreenshotService();
        _googlePlayViewModel = googlePlayViewModel ?? GooglePlayFeatureFactory.Create(
            publisherService,
            desktopInteractionService,
            () => SelectedSection = PublishSection.Signing);
        _nugetViewModel = nugetViewModel ?? NuGetFeatureFactory.Create(
            publisherService,
            desktopInteractionService,
            AppendLog);
        AttachNuGetNotifications();
        EnsureXamlBindingsPreserved();
        IsProjectInternalVersionSupported = GetDefaultInternalVersionSupport(PublishPlatform);
        DotnetStatus = publisherService.DotnetStatusText;
        AdbStatus = publisherService.AdbStatusText;
        AdbInstallationNote = BuildAdbInstallationNote(publisherService);
        EmulatorStatus = publisherService.EmulatorStatusText;
        StatusMessage = "Select a .NET project folder to start building your publish command.";
        SelectedNavItem = NavItems.FirstOrDefault();
        RefreshCommandPreview();
        if (!OperatingSystem.IsBrowser())
        {
            _ = RefreshEmulatorsAsync();
        }
        else
        {
            EmulatorStatus = "Device actions are available in the desktop app.";
        }
    }

    // Keep command properties referenced from code so trimmed builds retain members used only from XAML.
    private void EnsureXamlBindingsPreserved()
    {
        _ = BrowseProjectDirectoryCommand;
        _ = ReloadProjectMetadataCommand;
        _ = RefreshDevicesCommand;
        _ = RefreshIosSimulatorsCommand;
        _ = LaunchDeviceCommand;
        _ = LaunchIosSimulatorCommand;
        _ = PublishCommand;
        _ = CancelPublishCommand;
        _ = OpenPublishFolderCommand;
        _ = InstallLatestApkCommand;
        _ = UninstallAppCommand;
        _ = LaunchAppCommand;
        _ = CloseAppCommand;
        _ = CopyFileToAndroidDeviceCommand;
        _ = InstallIosAppCommand;
        _ = UninstallIosAppCommand;
        _ = LaunchIosAppCommand;
        _ = StopIosAppCommand;
        _ = CopyFileToIosDeviceCommand;
        _ = DeleteDesktopAppCommand;
        _ = CopyCommandPreviewCommand;
        _ = ToggleAlertSoundCommand;
        _ = CopyKnownGoodBaselineCommand;
        _ = CopyLiveOutputCommand;
        _ = CopyWindowScreenshotCommand;
        _ = SaveWindowScreenshotToDiskCommand;
        _ = BrowseOutputDirectoryCommand;
        _ = BrowseKeystoreCommand;
        _ = ResetToSafeDefaultsCommand;
        _ = ToggleThemeCommand;
        _ = RefreshDeviceFilesCommand;
        _ = GoUpDeviceFilesCommand;
        _ = GoToDeviceLocationCommand;
        _ = OpenDeviceFilesPathCommand;
        _ = SendFilesToDeviceCommand;
        _ = RefreshWirelessPeersCommand;
        _ = PairWirelessDeviceCommand;
        _ = ConnectWirelessDeviceCommand;
        _ = DisconnectWirelessDeviceCommand;
        _ = EnableWirelessDebuggingCommand;
        _ = ResetWirelessDebuggingCommand;
        _ = RefreshIosDevicesCommand;
        _ = InstallIosDeviceAppCommand;
        _ = LaunchIosDeviceAppCommand;
        _ = ApplyPublishOptionSuggestionCommand;
        _ = InspectInstalledAndroidPackageCommand;
        _ = IsPublishing;
        _ = GooglePlay;
        _ = NuGet;
    }

    public GooglePlayViewModel GooglePlay => _googlePlayViewModel;

    public IReadOnlyList<string> PublishPlatformOptions { get; } =
    [
        PublisherService.AndroidPlatform,
        PublisherService.MacOsPlatform,
        PublisherService.WindowsPlatform,
        PublisherService.IosPlatform,
        PublisherService.LinuxPlatform,
        PublisherService.NuGetPlatform
    ];

    public IReadOnlyList<string> ConfigurationOptions { get; } = ["Release", "Debug"];

    public IReadOnlyList<string> LinkModeOptions { get; } = ["None", "SdkOnly", "Full"];

    public IReadOnlyList<string> LinkToolOptions { get; } = ["r8"];

    public IReadOnlyList<string> DexToolOptions { get; } = ["d8"];

    public IReadOnlyList<string> SignModeOptions { get; } = ["Auto", "Sign", "Do Not Sign"];

    public IReadOnlyList<string> ThemeOptions { get; } = ["System", "Light", "Dark"];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallLatestApkCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(LaunchAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteDesktopAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshEmulatorsCommand))]
    [NotifyCanExecuteChangedFor(nameof(LaunchEmulatorCommand))]
    [NotifyPropertyChangedFor(nameof(IsAndroidPlatform))]
    [NotifyPropertyChangedFor(nameof(IsMacOsPlatform))]
    [NotifyPropertyChangedFor(nameof(IsLinuxPlatform))]
    [NotifyCanExecuteChangedFor(nameof(OpenPublishFolderCommand))]
    [NotifyPropertyChangedFor(nameof(IsNuGetPlatform))]
    [NotifyPropertyChangedFor(nameof(IsAppBuildOptionsVisible))]
    [NotifyPropertyChangedFor(nameof(IsNuGetDeployCardVisible))]
    [NotifyPropertyChangedFor(nameof(IsCrashLogsCardVisible))]
    [NotifyPropertyChangedFor(nameof(IsDeviceFilesFallbackVisible))]
    [NotifyPropertyChangedFor(nameof(IsPlatformSigningCardVisible))]
    [NotifyPropertyChangedFor(nameof(IsAndroidActionsVisible))]
    [NotifyPropertyChangedFor(nameof(IsDeviceFilesAvailable))]
    [NotifyPropertyChangedFor(nameof(IosDeviceSupportNote))]
    [NotifyPropertyChangedFor(nameof(IsDesktopActionsVisible))]
    [NotifyPropertyChangedFor(nameof(IsAndroidPublishOptionsVisible))]
    [NotifyPropertyChangedFor(nameof(IsMacOsPublishOptionsVisible))]
    [NotifyPropertyChangedFor(nameof(IsWindowsPublishOptionsVisible))]
    [NotifyPropertyChangedFor(nameof(IsIosPublishOptionsVisible))]
    [NotifyPropertyChangedFor(nameof(IsLinuxPublishOptionsVisible))]
    [NotifyPropertyChangedFor(nameof(IsSigningCardVisible))]
    [NotifyPropertyChangedFor(nameof(IsKnownGoodBaselineVisible))]
    [NotifyPropertyChangedFor(nameof(ProjectInternalVersionLabel))]
    [NotifyPropertyChangedFor(nameof(IsAppVersionVisible))]
    [NotifyPropertyChangedFor(nameof(IsPublishAotEnabled))]
    [NotifyPropertyChangedFor(nameof(PublishAotDisabledReason))]
    [NotifyPropertyChangedFor(nameof(IsPublishTrimmedEnabled))]
    [NotifyPropertyChangedFor(nameof(PublishTrimmedDisabledReason))]
    [NotifyPropertyChangedFor(nameof(IsReadyToRunCheckBoxEnabled))]
    [NotifyPropertyChangedFor(nameof(ReadyToRunDisabledReason))]
    [NotifyPropertyChangedFor(nameof(IsProfiledAotEnabled))]
    [NotifyPropertyChangedFor(nameof(ProfiledAotDisabledReason))]
    [NotifyPropertyChangedFor(nameof(IsPublishSingleFileEnabled))]
    [NotifyPropertyChangedFor(nameof(PublishSingleFileDisabledReason))]
    [NotifyPropertyChangedFor(nameof(IsUseAppHostEnabled))]
    [NotifyPropertyChangedFor(nameof(IsIosDeviceRuntime))]
    [NotifyPropertyChangedFor(nameof(ShowAndroidPackageFormatsTogether))]
    [NotifyPropertyChangedFor(nameof(IsDeviceWatchStatusVisible))]
    private string _publishPlatform = PublisherService.AndroidPlatform;

[ObservableProperty]
[NotifyCanExecuteChangedFor(nameof(ReloadProjectMetadataCommand))]
[NotifyCanExecuteChangedFor(nameof(PublishCommand))]
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
    private string _projectOutputLayoutSummary = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    [NotifyPropertyChangedFor(nameof(IsIosDeviceRuntime))]
    [NotifyPropertyChangedFor(nameof(IsPublishAotEnabled))]
    [NotifyPropertyChangedFor(nameof(PublishAotDisabledReason))]
    [NotifyPropertyChangedFor(nameof(IsPublishTrimmedEnabled))]
    [NotifyPropertyChangedFor(nameof(PublishTrimmedDisabledReason))]
    private string _targetFramework = "net10.0-android";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    [NotifyPropertyChangedFor(nameof(IsIosDeviceRuntime))]
    [NotifyPropertyChangedFor(nameof(IsPublishAotEnabled))]
    [NotifyPropertyChangedFor(nameof(PublishAotDisabledReason))]
    [NotifyPropertyChangedFor(nameof(IsPublishTrimmedEnabled))]
    [NotifyPropertyChangedFor(nameof(PublishTrimmedDisabledReason))]
    [NotifyPropertyChangedFor(nameof(IsUseAppHostEnabled))]
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
    [NotifyCanExecuteChangedFor(nameof(CloseAppCommand))]
    private string _packageId = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private bool _includeApk = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    [NotifyPropertyChangedFor(nameof(ShowAndroidPackageFormatsTogether))]
    private bool _includeAab;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    [NotifyPropertyChangedFor(nameof(IsPublishTrimmedEnabled))]
    [NotifyPropertyChangedFor(nameof(PublishTrimmedDisabledReason))]
    [NotifyPropertyChangedFor(nameof(IsPublishAotEnabled))]
    [NotifyPropertyChangedFor(nameof(PublishAotDisabledReason))]
    [NotifyPropertyChangedFor(nameof(IsPublishSingleFileEnabled))]
    [NotifyPropertyChangedFor(nameof(PublishSingleFileDisabledReason))]
    private bool _selfContained = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    [NotifyPropertyChangedFor(nameof(IsPublishTrimmedEnabled))]
    [NotifyPropertyChangedFor(nameof(PublishTrimmedDisabledReason))]
    [NotifyPropertyChangedFor(nameof(IsPublishAotEnabled))]
    [NotifyPropertyChangedFor(nameof(PublishAotDisabledReason))]
    private bool _publishTrimmed;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    [NotifyPropertyChangedFor(nameof(IsPublishTrimmedEnabled))]
    [NotifyPropertyChangedFor(nameof(PublishTrimmedDisabledReason))]
    [NotifyPropertyChangedFor(nameof(IsReadyToRunCheckBoxEnabled))]
    [NotifyPropertyChangedFor(nameof(ReadyToRunDisabledReason))]
    [NotifyPropertyChangedFor(nameof(IsPublishAotEnabled))]
    [NotifyPropertyChangedFor(nameof(PublishAotDisabledReason))]
    private bool _publishAot;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    [NotifyPropertyChangedFor(nameof(IsReadyToRunCheckBoxEnabled))]
    [NotifyPropertyChangedFor(nameof(ReadyToRunDisabledReason))]
    private bool _publishReadyToRun;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    [NotifyPropertyChangedFor(nameof(IsPublishSingleFileEnabled))]
    [NotifyPropertyChangedFor(nameof(PublishSingleFileDisabledReason))]
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
    [NotifyPropertyChangedFor(nameof(IsProfiledAotEnabled))]
    [NotifyPropertyChangedFor(nameof(ProfiledAotDisabledReason))]
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
    private string _knownGoodBaselineCommand = string.Empty;

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

    /// <summary>
    /// The optional companion the publish option graph offered. The user turns it on with
    /// <see cref="ApplyPublishOptionSuggestionCommand"/> when they want it.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPublishOptionSuggestion))]
    [NotifyPropertyChangedFor(nameof(PublishOptionSuggestionText))]
    private PublishOptionId? _pendingPublishOptionSuggestion;

    /// <summary>True when the option graph offered an optional companion the user can take.</summary>
    public bool HasPublishOptionSuggestion => PendingPublishOptionSuggestion is not null;

    /// <summary>Explains what the option graph offered and why it is worth turning on.</summary>
    public string PublishOptionSuggestionText => PendingPublishOptionSuggestion is { } option
        ? $"{DescribePublishOption(option)} can be turned on as well. {_pendingPublishOptionSuggestionReason}"
        : string.Empty;

    private string _pendingPublishOptionSuggestionReason = string.Empty;

    public ObservableCollection<string> AvailableEmulators { get; } = [];
    public ObservableCollection<AndroidDeviceInfo> AvailableAndroidDevices { get; } = [];
    public ObservableCollection<string> AvailableIosSimulators { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallIosAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallIosAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(LaunchIosAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopIosAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyFileToIosDeviceCommand))]
    private string _selectedIosSimulator = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReloadProjectMetadataCommand))]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenPublishFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallLatestApkCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(LaunchAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteDesktopAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyWindowScreenshotCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveWindowScreenshotToDiskCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyCommandPreviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyKnownGoodBaselineCommand))]
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
    [NotifyCanExecuteChangedFor(nameof(StopIosAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyFileToAndroidDeviceCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyFileToIosDeviceCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelPublishCommand))]
    [NotifyPropertyChangedFor(nameof(IsEditorEnabled))]
    [NotifyPropertyChangedFor(nameof(IsReadyToRunCheckBoxEnabled))]
    [NotifyPropertyChangedFor(nameof(IsSharedProgressVisible))]
    [NotifyPropertyChangedFor(nameof(IsSharedProgressIndeterminate))]
    [NotifyPropertyChangedFor(nameof(SharedProgressText))]
    [NotifyPropertyChangedFor(nameof(SharedProgressValue))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isPublishing;

    public bool IsEditorEnabled => !IsBusy;

    public bool IsSharedProgressVisible => IsBusy;

    public bool IsSharedProgressIndeterminate => IsBusy && !IsScreenshotCountdownVisible;

    public string SharedProgressText => IsScreenshotCountdownVisible ? ScreenshotCountdownText : string.Empty;

    public double SharedProgressValue => IsScreenshotCountdownVisible ? ScreenshotCountdownProgress : 0;

    public bool HasProjectSelection => !string.IsNullOrWhiteSpace(ProjectDirectory);

    public string AppDisplayVersion { get; } =
        typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) is { Length: > 0 } version
            ? $"v{version}"
            : "v0.0.0";

    public bool IsAndroidPlatform => string.Equals(PublishPlatform, PublisherService.AndroidPlatform, StringComparison.Ordinal);

    public bool IsMacOsPlatform => string.Equals(PublishPlatform, PublisherService.MacOsPlatform, StringComparison.Ordinal);

    public bool IsIosPlatform => string.Equals(PublishPlatform, PublisherService.IosPlatform, StringComparison.Ordinal);

    public bool IsLinuxPlatform => string.Equals(PublishPlatform, PublisherService.LinuxPlatform, StringComparison.Ordinal);

    /// <summary>NuGet platform: the app publishes a package, not an app, so Publish packs a .nupkg.</summary>
    public bool IsNuGetPlatform => string.Equals(PublishPlatform, PublisherService.NuGetPlatform, StringComparison.Ordinal);

    public bool IsAndroidActionsVisible => IsAndroidPlatform;

    public bool IsDesktopActionsVisible => IsMacOsPlatform;

    public bool IsAndroidPublishOptionsVisible => IsAndroidPlatform;

    public bool IsMacOsPublishOptionsVisible => IsMacOsPlatform;

    public bool IsWindowsPublishOptionsVisible => string.Equals(PublishPlatform, PublisherService.WindowsPlatform, StringComparison.Ordinal);

    public bool IsIosPublishOptionsVisible => string.Equals(PublishPlatform, PublisherService.IosPlatform, StringComparison.Ordinal);

    public bool IsLinuxPublishOptionsVisible => IsLinuxPlatform;

    public bool IsIosActionsVisible => IsIosPublishOptionsVisible;

    public bool IsPackageIdVisible => true;

    public bool ShowAndroidPackageFormatsTogether => IsAndroidPlatform && IncludeAab;

    public bool IsIosDeviceRuntime => string.Equals(PublishPlatform, PublisherService.IosPlatform, StringComparison.Ordinal)
        && !RuntimeIdentifier.StartsWith("iossimulator-", StringComparison.OrdinalIgnoreCase);

    public bool IsSigningEnabled => string.Equals(SignMode, "Sign", StringComparison.Ordinal);

    public bool IsSigningCardVisible => IsAndroidPlatform && IsSigningEnabled;

    public bool IsKnownGoodBaselineVisible => true;

    public bool IsShrinkerSettingsEnabled => PublishOptionPolicy.IsShrinkerSettingsEnabled(AndroidLinkMode);

    public bool IsPublishAotEnabled => PublishOptionPolicy.IsPublishAotEnabled(PublishPlatform, TargetFramework, RuntimeIdentifier);
    public string PublishAotDisabledReason => PublishOptionPolicy.PublishAotDisabledReason(PublishPlatform, TargetFramework, RuntimeIdentifier);

    public bool IsPublishTrimmedEnabled => PublishOptionPolicy.IsPublishTrimmedEnabled(PublishAot, SelfContained, PublishPlatform, TargetFramework, RuntimeIdentifier, AndroidLinkMode);
    public string PublishTrimmedDisabledReason => PublishOptionPolicy.PublishTrimmedDisabledReason(PublishAot, SelfContained, PublishPlatform, TargetFramework, RuntimeIdentifier, AndroidLinkMode);

    public bool IsProfiledAotEnabled => PublishOptionPolicy.IsProfiledAotEnabled(RunAotCompilation);
    public string ProfiledAotDisabledReason => PublishOptionPolicy.ProfiledAotDisabledReason(RunAotCompilation);

    public bool IsRunAotEnabled => !IsAndroidPlatform || PublishOptionPolicy.IsAndroidAotEnabled(AndroidLinkMode);
    public string RunAotDisabledReason => PublishOptionPolicy.AndroidAotDisabledReason(AndroidLinkMode);

    public bool IsPublishSingleFileEnabled => PublishOptionPolicy.IsPublishSingleFileEnabled(PublishAot, PublishPlatform);

    public string PublishSingleFileDisabledReason => PublishAot
        ? "Native AOT already produces a native executable; single-file bundling is not needed."
        : IsPublishSingleFileEnabled ? string.Empty : "SingleFile not applicable for this platform.";

    public bool IsUseAppHostEnabled => PublishOptionPolicy.IsUseAppHostEnabled(PublishPlatform);

    public string ReadyToRunDisabledReason => PublishOptionPolicy.ReadyToRunDisabledReason(PublishAot, PublishPlatform, TargetFramework, RuntimeIdentifier);

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
            // Context first: the NuGet pack preview reads the context, so the output preview must refresh after it.
            UpdateGooglePlayContext();
            RefreshCommandPreview();
        }
    }

    private void OnSectionEntered(PublishSection section)
    {
        // The Android target list keeps itself current while a section that shows targets is open, and
        // stops polling as soon as the user moves somewhere that cannot use a device.
        if (section is PublishSection.Platform or PublishSection.Deploy or PublishSection.DeviceFiles)
        {
            StartDeviceWatch();
        }
        else
        {
            StopDeviceWatch();
        }

        // Keep device/simulator lists fresh when entering the sections that use them.
        // Only auto-discover when idle and the backing tool exists so section entry never throws.
        if (section is PublishSection.Platform or PublishSection.Deploy or PublishSection.DeviceFiles && !IsBusy)
        {
            if (IsAndroidPlatform && _publisherService.AdbPath is not null && AvailableAndroidDevices.Count == 0)
            {
                _ = RefreshDevicesAsync();
            }

            if (IsIosPlatform && _publisherService.XcrunPath is not null && AvailableIosSimulators.Count == 0)
            {
                _ = RefreshIosSimulatorsAsync();
            }

            if (IsIosPlatform)
            {
                _ = AutoDiscoverIosDevicesAsync();
            }

            if (section == PublishSection.DeviceFiles)
            {
                _ = LoadDeviceFilesFromSectionAsync();
            }
        }
    }

    /// <summary>Keeps the open folder in sync when the user switches target.</summary>
    partial void OnSelectedDeviceChanged(AndroidDeviceInfo? value)
    {
        OnPropertyChanged(nameof(DeviceFilesHint));
        if (IsDeviceFilesSection)
        {
            _ = LoadDeviceFilesFromSectionAsync();
        }
    }

    partial void OnProjectDirectoryChanged(string value)
    {
        _projectOutputLayout = null;
        ProjectOutputLayoutSummary = string.Empty;
        SetAutomaticOutputDirectory(string.Empty);
        RefreshAvailableProjectFiles();
        UpdateGooglePlayContext();
    }

    partial void OnSelectedProjectFileChanged(string value)
    {
        // Picking another discovered project reloads its metadata; the reload assigns the same
        // value back, which fires no further change.
        if (_isReloadingMetadata || string.IsNullOrWhiteSpace(value) || !File.Exists(value))
        {
            return;
        }

        if (IsProjectFileUnderSelectedDirectory(value))
        {
            _ = ReloadProjectMetadataAsync();
        }
    }

    private void RefreshAvailableProjectFiles()
    {
        AvailableProjectFiles.Clear();
        if (!string.IsNullOrWhiteSpace(ProjectDirectory) && Directory.Exists(ProjectDirectory))
        {
            foreach (var file in ProjectInspector.DiscoverProjectFiles(new DirectoryInfo(ProjectDirectory)))
            {
                AvailableProjectFiles.Add(file.FullName);
            }
        }

        OnPropertyChanged(nameof(HasMultipleProjectFiles));
    }

    private bool IsProjectFileUnderSelectedDirectory(string projectFilePath)
    {
        if (string.IsNullOrWhiteSpace(ProjectDirectory))
        {
            return false;
        }

        var root = ProjectDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return projectFilePath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The project to build: the picked one when it belongs to the selected folder, else the first discovered.</summary>
    internal string? ResolveProjectFilePath()
    {
        if (!string.IsNullOrWhiteSpace(SelectedProjectFile)
            && File.Exists(SelectedProjectFile)
            && IsProjectFileUnderSelectedDirectory(SelectedProjectFile))
        {
            return SelectedProjectFile;
        }

        if (!string.IsNullOrWhiteSpace(ProjectDirectory) && Directory.Exists(ProjectDirectory))
        {
            var discovered = ProjectInspector.DiscoverProjectFiles(new DirectoryInfo(ProjectDirectory));
            if (discovered.Count > 0)
            {
                return discovered[0].FullName;
            }
        }

        return null;
    }

    partial void OnOutputDirectoryChanged(string value)
    {
        if (!_isAssigningAutomaticOutputDirectory)
        {
            _isOutputDirectoryManualOverride = !string.IsNullOrWhiteSpace(value);
        }

        UpdateGooglePlayContext();
    }

    partial void OnPublishPlatformChanged(string value)
    {
        IsProjectInternalVersionSupported = GetDefaultInternalVersionSupport(value);

        // Platforms other than Android have nothing to watch, so this stops the poll as well.
        StartDeviceWatch();

        ApplyPlatformProfiles(value);

        if (!_isOutputDirectoryManualOverride)
        {
            SetAutomaticOutputDirectory(string.Empty);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsReadyToRunEnabled)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsReadyToRunCheckBoxEnabled)));
        UpdateGooglePlayContext();

        if (!string.IsNullOrWhiteSpace(ProjectDirectory))
        {
            _ = ReloadProjectMetadataAsync();
        }
    }

    private void ApplyPlatformProfiles(string platform)
    {
        var defaults = PlatformProfiles.GetDefaults(platform);
        TargetFramework = defaults.TargetFramework;
        RuntimeIdentifier = defaults.RuntimeIdentifier;
        SelfContained = defaults.SelfContained;
        PublishTrimmed = defaults.PublishTrimmed;
        PublishAot = defaults.PublishAot;
        PublishReadyToRun = defaults.PublishReadyToRun;
        PublishSingleFile = defaults.PublishSingleFile;
        UseAppHost = defaults.UseAppHost;
        CreateMacAppBundle = defaults.CreateMacAppBundle;
        CreateWindowsExecutable = defaults.CreateWindowsExecutable;
        BuildIpa = defaults.BuildIpa;
        ArchiveOnBuild = defaults.ArchiveOnBuild;
        RunAotCompilation = defaults.RunAotCompilation;
        EnableProfiledAot = defaults.EnableProfiledAot;
        AndroidLinkMode = defaults.AndroidLinkMode;
        AndroidLinkTool = defaults.AndroidLinkTool;
        AndroidDexTool = defaults.AndroidDexTool;
        CreateMappingFile = defaults.CreateMappingFile;
        EnableMultiDex = defaults.EnableMultiDex;
        UseAapt2 = defaults.UseAapt2;
        EnableDesugar = defaults.EnableDesugar;
        IncludeApk = defaults.IncludeApk;
        IncludeAab = defaults.IncludeAab;
        SignMode = defaults.SignMode;
        NormalizePublishSettings();
    }

    /// <summary>
    /// Applies the publish option graph. The graph decides which options follow each other: a
    /// required companion is switched on with the option that needs it, an option whose
    /// prerequisite was switched off goes with it, and two mutually exclusive options never stay
    /// on together. The applied changes are reported so the user sees what the graph did.
    /// </summary>
    private void NormalizePublishSettings(PublishOptionId? changedOption = null, bool changedValue = true)
    {
        if (_isNormalizingPublishSettings)
        {
            return;
        }

        var resolution = PublishOptionResolver.Resolve(
            BuildPublishSettingsState(),
            changedOption,
            changedValue);

        _isNormalizingPublishSettings = true;
        try
        {
            SelfContained = resolution.State.SelfContained;
            PublishAot = resolution.State.PublishAot;
            PublishReadyToRun = resolution.State.PublishReadyToRun;
            PublishSingleFile = resolution.State.PublishSingleFile;
            PublishTrimmed = resolution.State.PublishTrimmed;
            AndroidLinkMode = resolution.State.AndroidLinkMode;
            AndroidLinkTool = resolution.State.AndroidLinkTool;
            AndroidDexTool = resolution.State.AndroidDexTool;
            RunAotCompilation = resolution.State.RunAotCompilation;
            EnableProfiledAot = resolution.State.EnableProfiledAot;
            IncludeApk = resolution.State.IncludeApk;
            IncludeAab = resolution.State.IncludeAab;
            UseAppHost = resolution.State.UseAppHost;
            CreateMacAppBundle = resolution.State.CreateMacAppBundle;
            CreateWindowsExecutable = resolution.State.CreateWindowsExecutable;
            BuildIpa = resolution.State.BuildIpa;
            ArchiveOnBuild = resolution.State.ArchiveOnBuild;
            CreateMappingFile = resolution.State.CreateMappingFile;
            EnableMultiDex = resolution.State.EnableMultiDex;
            UseAapt2 = resolution.State.UseAapt2;
            EnableDesugar = resolution.State.EnableDesugar;
        }
        finally
        {
            _isNormalizingPublishSettings = false;
        }

        ReportPublishOptionChanges(resolution);
    }

    private PublishSettingsState BuildPublishSettingsState() => new(
        PublishPlatform,
        TargetFramework,
        RuntimeIdentifier,
        SelfContained,
        PublishAot,
        PublishReadyToRun,
        PublishSingleFile,
        PublishTrimmed,
        AndroidLinkMode,
        AndroidLinkTool,
        AndroidDexTool,
        RunAotCompilation,
        EnableProfiledAot,
        IncludeApk,
        IncludeAab,
        UseAppHost,
        CreateMacAppBundle,
        CreateWindowsExecutable,
        BuildIpa,
        ArchiveOnBuild,
        CreateMappingFile,
        EnableMultiDex,
        UseAapt2,
        EnableDesugar);

    /// <summary>Tells the user what the option graph changed and what it can still offer.</summary>
    private void ReportPublishOptionChanges(PublishOptionResolution resolution)
    {
        if (resolution.Changes.Count > 0)
        {
            var applied = string.Join(
                " ",
                resolution.Changes.Select(change =>
                    $"{DescribePublishOption(change.Option)} {(change.Value ? "on" : "off")}"));

            StatusMessage = $"Adjusted the publish options: {applied}.";
        }

        if (resolution.Suggestions.Count > 0)
        {
            var suggestion = resolution.Suggestions[0];
            StatusMessage =
                $"{DescribePublishOption(suggestion.Option)} can be turned on as well. {suggestion.Reason}";
            _pendingPublishOptionSuggestionReason = suggestion.Reason;
            PendingPublishOptionSuggestion = suggestion.Option;
        }
        else
        {
            PendingPublishOptionSuggestion = null;
            _pendingPublishOptionSuggestionReason = string.Empty;
        }
    }

    private static string DescribePublishOption(PublishOptionId option) => option switch
    {
        PublishOptionId.SelfContained => "Self-contained",
        PublishOptionId.PublishTrimmed => "Publish trimmed",
        PublishOptionId.PublishAot => "Native AOT",
        PublishOptionId.PublishReadyToRun => "ReadyToRun",
        PublishOptionId.PublishSingleFile => "Single-file",
        PublishOptionId.UseAppHost => "Use app host",
        PublishOptionId.CreateMacAppBundle => "Create .app bundle",
        PublishOptionId.CreateWindowsExecutable => "Create .exe",
        PublishOptionId.BuildIpa => "Build IPA",
        PublishOptionId.ArchiveOnBuild => "Archive on build",
        PublishOptionId.RunAotCompilation => "RunAOT",
        PublishOptionId.EnableProfiledAot => "Profiled AOT",
        PublishOptionId.CreateMappingFile => "Create mapping file",
        PublishOptionId.EnableMultiDex => "Enable MultiDex",
        PublishOptionId.UseAapt2 => "Use AAPT2",
        PublishOptionId.EnableDesugar => "Enable Desugar",
        PublishOptionId.IncludeApk => "Generate APK",
        PublishOptionId.IncludeAab => "Generate AAB",
        PublishOptionId.AndroidLinkMode => "Link Mode",
        PublishOptionId.AndroidLinkTool => "Link Tool",
        PublishOptionId.AndroidDexTool => "Dex Tool",
        _ => option.ToString(),
    };

    /// <summary>Turns on the optional companion the option graph offered.</summary>
    [RelayCommand]
    private void ApplyPublishOptionSuggestion()
    {
        if (PendingPublishOptionSuggestion is not { } option)
        {
            return;
        }

        switch (option)
        {
            case PublishOptionId.SelfContained: SelfContained = true; break;
            case PublishOptionId.PublishTrimmed: PublishTrimmed = true; break;
            case PublishOptionId.PublishAot: PublishAot = true; break;
            case PublishOptionId.PublishReadyToRun: PublishReadyToRun = true; break;
            case PublishOptionId.PublishSingleFile: PublishSingleFile = true; break;
            case PublishOptionId.UseAppHost: UseAppHost = true; break;
            case PublishOptionId.CreateMacAppBundle: CreateMacAppBundle = true; break;
            case PublishOptionId.CreateWindowsExecutable: CreateWindowsExecutable = true; break;
            case PublishOptionId.BuildIpa: BuildIpa = true; break;
            case PublishOptionId.ArchiveOnBuild: ArchiveOnBuild = true; break;
            case PublishOptionId.RunAotCompilation: RunAotCompilation = true; break;
            case PublishOptionId.EnableProfiledAot: EnableProfiledAot = true; break;
            case PublishOptionId.CreateMappingFile: CreateMappingFile = true; break;
            case PublishOptionId.EnableMultiDex: EnableMultiDex = true; break;
            case PublishOptionId.UseAapt2: UseAapt2 = true; break;
            case PublishOptionId.EnableDesugar: EnableDesugar = true; break;
            case PublishOptionId.IncludeApk: IncludeApk = true; break;
            case PublishOptionId.IncludeAab: IncludeAab = true; break;
        }

        PendingPublishOptionSuggestion = null;
    }

    [RelayCommand]
    private void ResetToSafeDefaults()
    {
        ApplyPlatformProfiles(PublishPlatform);
        StatusMessage = $"Reset {PublishPlatform} to known-good baseline options.";
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
    private void ToggleTheme()
    {
        Theme = Theme switch
        {
            "Light" => "Dark",
            "Dark" => "System",
            _ => "Light"
        };
    }

    [RelayCommand]
    private async Task BrowseProjectDirectoryAsync()
    {
        if (OperatingSystem.IsBrowser())
        {
            StatusMessage = "Project browsing is available in the desktop app.";
            return;
        }

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
        if (OperatingSystem.IsBrowser())
        {
            StatusMessage = "Output folder browsing is available in the desktop app.";
            return;
        }

        var selected = await _desktopInteractionService.PickFolderAsync("Select publish output directory", OutputDirectory);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            OutputDirectory = selected;
        }
    }

    [RelayCommand]
    private async Task BrowseKeystoreAsync()
    {
        if (OperatingSystem.IsBrowser())
        {
            StatusMessage = "Keystore browsing is available in the desktop app.";
            return;
        }

        var selected = await _desktopInteractionService.PickFileAsync("Select signing keystore", KeystorePath);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            KeystorePath = selected;
        }
    }

    /// <summary>
    /// Names the older adb copies on this computer that the app does not use, and how each is removed.
    /// The app leaves them where they are: a Homebrew cask or an IDE owns those files, and deleting
    /// another installer's files would leave that installer's records wrong.
    /// </summary>
    private static string BuildAdbInstallationNote(PublisherService publisherService)
    {
        var unused = publisherService.UnusedAdbNotes;
        if (unused.Count == 0)
        {
            return string.Empty;
        }

        var lines = string.Join("; ", unused);
        return unused.Count == 1
            ? $"This computer also has an older adb the app does not use: {lines}."
            : $"This computer also has {unused.Count} older adb builds the app does not use: {lines}.";
    }

    /// <summary>Empty when this computer has only the adb the app runs.</summary>
    [ObservableProperty]
    private string _adbInstallationNote = string.Empty;

    [RelayCommand(CanExecute = nameof(CanRefreshEmulators))]
    private async Task RefreshEmulatorsAsync()
    {
        if (OperatingSystem.IsBrowser())
        {
            StatusMessage = "Android emulator discovery is available in the desktop app.";
            return;
        }

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
        if (OperatingSystem.IsBrowser())
        {
            StatusMessage = "Android device actions are available in the desktop app.";
            return;
        }

        try
        {
            IsBusy = true;
            var devices = await _publisherService.DiscoverAndroidDevicesAsync(CancellationToken.None);
            var change = ApplyDiscoveredDevices(devices);

            var changeText = AndroidDeviceWatch.DescribeChange(change);
            StatusMessage = AvailableAndroidDevices.Count == 0
                ? "No Android devices discovered. Connect a device or launch an emulator, then refresh."
                : changeText is not null
                    ? changeText
                    : SelectedDevice is null
                        ? $"Discovered {AvailableAndroidDevices.Count} Android target(s). Select a device to use platform actions."
                        : $"Discovered {AvailableAndroidDevices.Count} Android target(s). Selected {SelectedDevice.DisplayName}.";
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
        if (OperatingSystem.IsBrowser())
        {
            StatusMessage = "iOS simulator actions are available in the desktop app.";
            return;
        }

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
                SelectedIosSimulator = AvailableIosSimulators[0];
                StatusMessage = $"Discovered {AvailableIosSimulators.Count} iOS simulator(s). Selected {SelectedIosSimulator}.";
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
            return;
        }

        var emulatorName = SelectedDevice.Name;
        var knownSerials = CaptureKnownSerials();
        var launched = await RunQuickActionAsync(
            async () => await _publisherService.LaunchEmulatorAsync(emulatorName, CancellationToken.None),
            "Launch emulator");
        if (launched)
        {
            // The list updates and selects the emulator on its own once it is up, so starting one never
            // needs a manual refresh.
            BeginWaitingForLaunchedTarget(emulatorName, emulatorName, knownSerials: knownSerials);
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
        return !OperatingSystem.IsBrowser() && IsIosPlatform && !IsBusy && !string.IsNullOrWhiteSpace(OutputDirectory) && !string.IsNullOrWhiteSpace(SelectedIosSimulator);
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

    [RelayCommand(CanExecute = nameof(CanUseIosPackageActions))]
    private async Task StopIosAppAsync()
    {
        await RunQuickActionAsync(
            async () => await _publisherService.StopIosAppAsync(PackageId, SelectedIosSimulator, CancellationToken.None),
            "Stop iOS app");
    }

    private bool CanUseIosPackageActions()
    {
        return !OperatingSystem.IsBrowser() && IsIosPlatform && !IsBusy && !string.IsNullOrWhiteSpace(PackageId) && !string.IsNullOrWhiteSpace(SelectedIosSimulator);
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
        return !OperatingSystem.IsBrowser() && IsAndroidPlatform && !IsBusy;
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
        return !OperatingSystem.IsBrowser() && IsIosPlatform && !IsBusy;
    }

    [RelayCommand]
    private void CancelPublish()
    {
        if (_publishCts is null)
        {
            return;
        }

        StatusMessage = "Cancelling publish...";
        _publishCts.Cancel();
    }

    public bool CanCancelPublish => _publishCts is not null;

    private void NotifyPublishStateChanged()
    {
        OnPropertyChanged(nameof(CanCancelPublish));
        CancelPublishCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanReloadProjectMetadata))]
    private async Task ReloadProjectMetadataAsync()
    {
        try
        {
            _isReloadingMetadata = true;
            var metadata = await _publisherService.LoadProjectMetadataAsync(
                ProjectDirectory, Configuration, TargetFramework, RuntimeIdentifier, PublishPlatform, ResolveProjectFilePath());
            SelectedProjectFile = metadata.ProjectFilePath;
            _projectOutputLayout = metadata.OutputLayout;
            ProjectOutputLayoutSummary = $"{metadata.OutputLayout.Description} Automatic destination: {metadata.DefaultOutputDirectory}";

            if (!_isOutputDirectoryManualOverride)
            {
                SetAutomaticOutputDirectory(metadata.DefaultOutputDirectory);
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
            else if (string.Equals(PublishPlatform, PublisherService.LinuxPlatform, StringComparison.Ordinal))
            {
                if (!RuntimeIdentifier.StartsWith("linux-", StringComparison.OrdinalIgnoreCase))
                {
                    RuntimeIdentifier = "linux-x64";
                }
            }
            else if (IsNuGetPlatform && !string.Equals(RuntimeIdentifier, "any", StringComparison.OrdinalIgnoreCase))
            {
                RuntimeIdentifier = "any";
            }

            StatusMessage = "Project metadata loaded. Review the command preview before publishing.";
        UpdateGooglePlayContext();
        }
        catch (Exception ex)
        {
            SelectedProjectFile = string.Empty;
            StatusMessage = "Could not load project metadata.";
            await _desktopInteractionService.ShowErrorAsync("Project metadata", ex.Message);
        }
        finally
        {
            _isReloadingMetadata = false;
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
        // On the NuGet platform "Publish" means "pack the NuGet package", so the header button
        // runs the NuGet pipeline instead of dotnet publish.
        if (IsNuGetPlatform)
        {
            IsPublishing = true;
            NotifyPublishStateChanged();
            try
            {
                await _nugetViewModel.RunPackAsync();
                StatusMessage = _nugetViewModel.StatusMessage;
            }
            finally
            {
                IsPublishing = false;
                NotifyPublishStateChanged();
            }

            return;
        }

        IsPublishing = true;
        IsBusy = true;
        ClearLog();
        AppendLog("Preparing publish workflow..." + Environment.NewLine);

        _publishCts = new CancellationTokenSource();
        NotifyPublishStateChanged();
        var token = _publishCts.Token;

        try
        {
            var success = await Task.Run(async () =>
            {
                return await _publisherService.PublishAsync(
                    CreateConfiguration(),
                    AppendLog,
                    token,
                    ConfirmWorkloadRestoreAsync);
            }, token);

            StatusMessage = success
                ? "Publish completed successfully." + BuildAndroidArtifactSummary()
                : BuildPublishFailureMessage();
            RefreshCommandPreview();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Publish cancelled.";
            AppendLog(Environment.NewLine + "=== Publish cancelled ===" + Environment.NewLine);
        }
        catch (Exception ex)
        {
            StatusMessage = "Publish could not start.";
            await _desktopInteractionService.ShowErrorAsync("Publish failed", ex.Message);
        }
        finally
        {
            FlushLogFinal();
            _publishCts?.Dispose();
            _publishCts = null;
            NotifyPublishStateChanged();
            IsPublishing = false;
            IsBusy = false;
        }
    }

    private bool CanPublish()
    {
        return !IsBusy && HasProjectSelection;
    }

    private async Task<bool> ConfirmWorkloadRestoreAsync(string question)
    {
        // PublishAsync runs on a background thread; dialogs must be shown on the UI thread.
        return await Dispatcher.UIThread.InvokeAsync(
            () => _desktopInteractionService.ConfirmAsync("Install missing workloads", question));
    }

    private string BuildAndroidArtifactSummary()
    {
        if (!IsAndroidPlatform)
        {
            return string.Empty;
        }

        var outputDirectory = OutputDirectory;
        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        {
            return string.Empty;
        }

        var apkCount = new DirectoryInfo(outputDirectory)
            .EnumerateFiles("*.apk", SearchOption.AllDirectories).Count();
        var aabCount = new DirectoryInfo(outputDirectory)
            .EnumerateFiles("*.aab", SearchOption.AllDirectories).Count();
        return $" Found {apkCount} APK and {aabCount} AAB in the output folder; check Open publish folder for each.";
    }
    private string BuildPublishFailureMessage()
    {
        var output = LiveOutput;

        if (output.Contains("Inadequate permissions", StringComparison.OrdinalIgnoreCase)
            || output.Contains("elevated privileges", StringComparison.OrdinalIgnoreCase))
        {
            return "Publish failed: workload restore needs admin rights. Run `sudo dotnet workload restore <project>` in Terminal, then publish again — or use the Known-Good Baseline (no AOT, no workload).";
        }

        var workloadMatch = Regex.Match(
            output,
            @"workloads must be installed:\s*(?<workloads>[^\r\n\[]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (output.Contains("NETSDK1147", StringComparison.OrdinalIgnoreCase) && workloadMatch.Success)
        {
            var workloads = workloadMatch.Groups["workloads"].Value.Trim();
            return $"Publish failed: missing workloads ({workloads}). " +
                "Run `sudo dotnet workload restore` in the project folder (needs admin on this machine), or use the Known-Good Baseline command (no AOT).";
        }

        if (output.Contains("NETSDK1147", StringComparison.OrdinalIgnoreCase))
        {
            return "Publish failed: missing workloads. Run `sudo dotnet workload restore` in the project folder, or use the Known-Good Baseline command (no AOT).";
        }

        if (output.Contains("NETSDK1042", StringComparison.OrdinalIgnoreCase))
        {
            return "Publish failed: target framework mismatch. Check Target Framework in the Build section against the project file.";
        }

        if (Regex.IsMatch(output, @"error XA\d{4}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return "Publish failed with an Android toolchain error. Check the live output for the XA error code.";
        }

        return "Publish failed. Check the live output for the exact toolchain error.";
    }

    [RelayCommand(CanExecute = nameof(CanOpenPublishFolder))]
    private async Task OpenPublishFolderAsync()
    {
        try
        {
            var folders = ResolvePublishArtifactFolders();
            foreach (var folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                _publisherService.OpenPublishFolder(folder);
            }
        }
        catch (Exception ex)
        {
            await _desktopInteractionService.ShowErrorAsync("Open publish folder", ex.Message);
        }
    }

    /// <summary>
    /// For Android, open the single folder that actually holds the published APK/AAB rather than a
    /// parent folder the user would have to dig through. Falls back to the configured output folder
    /// when no package has been published yet or the platform is not Android.
    /// </summary>
    internal IReadOnlyList<string> ResolvePublishArtifactFolders()
    {
        // On the NuGet platform the artifact is the .nupkg, so open the NuGet package folder
        // (the packed artifact's folder when a pack has run), not the app publish output.
        if (IsNuGetPlatform)
        {
            if (!string.IsNullOrWhiteSpace(NuGet.PackedPackagePath)
                && Path.GetDirectoryName(NuGet.PackedPackagePath) is { Length: > 0 } packedFolder
                && Directory.Exists(packedFolder))
            {
                return [packedFolder];
            }

            if (!string.IsNullOrWhiteSpace(NuGet.OutputDirectory) && Directory.Exists(NuGet.OutputDirectory))
            {
                return [NuGet.OutputDirectory.Trim()];
            }
        }

        var outputDirectory = OutputDirectory;
        if (IsAndroidPlatform && !string.IsNullOrWhiteSpace(outputDirectory) && Directory.Exists(outputDirectory))
        {
            var configuration = CreateConfiguration();
            var apk = ArtifactLocator.FindBestApk(outputDirectory, configuration.ProjectName, configuration.PackageId);
            var aab = ArtifactLocator.FindBestAab(outputDirectory, configuration.ProjectName, configuration.PackageId);

            var distinct = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var artifact in new[] { apk, aab })
            {
                if (artifact?.Directory?.FullName is { Length: > 0 } dir && seen.Add(dir))
                {
                    distinct.Add(dir);
                }
            }

            if (distinct.Count > 0)
            {
                return distinct;
            }
        }

        return string.IsNullOrWhiteSpace(outputDirectory) ? [] : new[] { outputDirectory .Trim() };
    }

    private string ResolvePublishArtifactFolder()
    {
        var outputDirectory = OutputDirectory;
        if (!IsAndroidPlatform || string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        {
            return outputDirectory;
        }

        var configuration = CreateConfiguration();
        var packageFolder = ArtifactLocator.FindBestPackageFolder(
            outputDirectory,
            configuration.ProjectName,
            configuration.PackageId);

        return packageFolder?.FullName ?? outputDirectory;
    }

    private bool CanOpenPublishFolder()
    {
        if (IsBusy)
        {
            return false;
        }

        if (IsNuGetPlatform)
        {
            return !string.IsNullOrWhiteSpace(NuGet.PackedPackagePath)
                || !string.IsNullOrWhiteSpace(NuGet.OutputDirectory);
        }

        return !string.IsNullOrWhiteSpace(OutputDirectory);
    }

    [RelayCommand(CanExecute = nameof(CanRunAndroidActions))]
    private async Task InstallLatestApkAsync()
    {
        if (!TryValidateAndroidDeviceSelection("Install APK"))
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

    /// <summary>
    /// Shows what the device has installed for the package: who installed it, the version code
    /// it runs, and the certificate it was signed with. The user needs this to know whether a
    /// Play Store update can replace the install, and which release line the device is on.
    ///
    /// The result also feeds the Google Play workspace, so its release line check knows the
    /// version code the device runs.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRunAndroidActions))]
    private async Task InspectInstalledAndroidPackageAsync()
    {
        if (!TryValidateAndroidDeviceSelection("Inspect installed app"))
        {
            return;
        }

        if (!TryValidatePackageId("Inspect installed app"))
        {
            return;
        }

        var installed = await _publisherService.ReadInstalledAndroidPackageAsync(
            PackageId,
            SelectedDevice?.Serial,
            CancellationToken.None);

        _googlePlayViewModel.SetConnectedDevice(installed is null
            ? null
            : new GooglePlayDeviceLineState(
                installed.VersionCode,
                installed.InstalledByGooglePlay,
                installed.CertificateDigests));

        await RunQuickActionAsync(
            async () => installed is null
                ? $"{PackageId} is not installed on {SelectedDevice?.Name}."
                : DescribeInstalledPackage(installed),
            "Inspect installed app");
    }

    /// <summary>Describes the installed app in plain words the user can act on.</summary>
    private static string DescribeInstalledPackage(AndroidInstalledPackageInfo installed)
    {
        var lines = new List<string>();
        lines.Add(installed.InstalledByGooglePlay
            ? "Installed by Google Play."
            : $"Installed by {installed.InstallerPackage ?? "an unknown source"}.");
        if (installed.VersionCode is { } versionCode)
        {
            lines.Add($"Version code {versionCode}.");
        }

        if (installed.CertificateDigests.Count > 0)
        {
            lines.Add($"Certificate SHA-256: {string.Join(", ", installed.CertificateDigests)}.");
        }

        lines.Add(installed.InstalledByGooglePlay
            ? "Google Play signs its downloads with the app signing key, so only a build with that same " +
              "certificate can be replaced by a Play Store update. Keep every new version code higher than this one."
            : "This copy was not installed by Google Play. Google Play can only update it when it carries the " +
              "same certificate the app signing key produces, and only with a higher version code.");
        return string.Join(" ", lines);
    }

    [RelayCommand(CanExecute = nameof(CanRunAndroidActions))]
    private async Task UninstallAppAsync()
    {
        if (!TryValidateAndroidDeviceSelection("Uninstall app"))
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

        if (!TryValidatePackageId("Launch app"))
        {
            return;
        }

        await RunQuickActionAsync(
            async () => await _publisherService.LaunchAsync(PackageId, SelectedDevice?.Serial, CancellationToken.None),
            "Launch app");
    }

    /// <summary>
    /// Closes the app on the selected Android device or emulator: every process of the package is
    /// stopped, and its card is dismissed from Recents (the app switcher) so the app is really gone
    /// instead of merely stopped. The installed app and its data are kept.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRunAndroidActions))]
    private async Task CloseAppAsync()
    {
        if (!TryValidateAndroidDeviceSelection("Close app"))
        {
            return;
        }

        if (!TryValidatePackageId("Close app"))
        {
            return;
        }

        await RunQuickActionAsync(
            async () => await _publisherService.CloseAsync(PackageId, SelectedDevice?.Serial, CancellationToken.None),
            "Close app");
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
            async () => await _screenshotService.CaptureWindowToClipboardAsync(_desktopInteractionService.Window, CancellationToken.None),
            "Screenshot to clipboard");
    }

    [RelayCommand(CanExecute = nameof(CanCopyWindowScreenshot))]
    private async Task SaveWindowScreenshotToDiskAsync()
    {
        await RunScreenshotActionAsync(
            async () => await _screenshotService.CaptureWindowToDiskAsync(_desktopInteractionService.Window, OutputDirectory, CancellationToken.None),
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
            var failed = IsFailureMessage(message);
            StatusMessage = failed
                ? $"{actionName} failed. Check Live Output for details."
                : $"✓ {actionName} complete!";
            
            AppendLog(Environment.NewLine + $"[{actionName}] {message}" + Environment.NewLine);
            PlayCompletionAlert(failed);
        }
        catch (Exception ex)
        {
            StatusMessage = $"{actionName} failed.";
            PlayCompletionAlert(failed: true);
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
        var emulatorName = SelectedEmulator;
        var knownSerials = CaptureKnownSerials();
        var launched = await RunQuickActionAsync(
            async () => await _publisherService.LaunchEmulatorAsync(emulatorName, CancellationToken.None),
            "Launch emulator");
        if (launched)
        {
            BeginWaitingForLaunchedTarget(emulatorName, emulatorName, knownSerials: knownSerials);
        }
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
        PlayCompletionAlert(failed: false);
    }

    private bool CanCopyCommandPreview()
    {
        return !IsBusy && !string.IsNullOrWhiteSpace(CommandPreview);
    }

    [RelayCommand(CanExecute = nameof(CanCopyKnownGoodBaseline))]
    private async Task CopyKnownGoodBaselineAsync()
    {
        await _desktopInteractionService.CopyTextToClipboardAsync(KnownGoodBaselineCommand);
        StatusMessage = "Known-good baseline command copied.";
        PlayCompletionAlert(failed: false);
    }

    private bool CanCopyKnownGoodBaseline()
    {
        return !IsBusy && !string.IsNullOrWhiteSpace(KnownGoodBaselineCommand);
    }

    [RelayCommand(CanExecute = nameof(CanCopyLiveOutput))]
    private async Task CopyLiveOutputAsync()
    {
        await _desktopInteractionService.CopyTextToClipboardAsync(LiveOutput);
        StatusMessage = "Live output copied.";
        PlayCompletionAlert(failed: false);
    }

    private bool CanCopyLiveOutput()
    {
        return !IsBusy && !string.IsNullOrWhiteSpace(LiveOutput);
    }

    private bool CanRefreshEmulators()
    {
        return !OperatingSystem.IsBrowser() && IsAndroidPlatform && !IsBusy;
    }

    private bool CanLaunchEmulator()
    {
        return !OperatingSystem.IsBrowser() && IsAndroidPlatform && !IsBusy && !string.IsNullOrWhiteSpace(SelectedEmulator);
    }

    /// <summary>Runs one device action. Returns true when it finished without a failure message.</summary>
    private async Task<bool> RunQuickActionAsync(Func<Task<string>> action, string actionName)
    {
        if (OperatingSystem.IsBrowser())
        {
            StatusMessage = $"{actionName} is available in the desktop app.";
            return false;
        }

        try
        {
            IsBusy = true;
            var message = await action();
            AppendLog(Environment.NewLine + $"[{actionName}] {message}" + Environment.NewLine);
            var failed = IsFailureMessage(message);
            StatusMessage = failed
                ? $"{actionName} failed. Check Live Output for details."
                : $"{actionName} finished.";
            PlayCompletionAlert(failed);
            return !failed;
        }
        catch (Exception ex)
        {
            StatusMessage = $"{actionName} failed.";
            PlayCompletionAlert(failed: true);
            await _desktopInteractionService.ShowErrorAsync(actionName, ex.Message);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// The one alert-sound player, shared with publishing and Google Play, so a single mute toggle
    /// silences every task alert in the app.
    /// </summary>
    private AlertSoundService AlertSounds => _publisherService.AlertSounds;

    /// <summary>
    /// Plays the alert that marks a finished task. Deploy actions, device file transfers, clipboard
    /// copies, screenshots, publishing, and Google Play all report through here.
    /// </summary>
    private void PlayCompletionAlert(bool failed) =>
        AlertSounds.Play(failed ? AlertTone.Failure : AlertTone.Success);

    /// <summary>Turn alerts off from the header when a quiet run is wanted.</summary>
    public bool IsAlertSoundEnabled
    {
        get => AlertSounds.IsEnabled;
        set
        {
            if (AlertSounds.IsEnabled == value)
            {
                return;
            }

            AlertSounds.IsEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AlertSoundToggleHint));
            if (!value)
            {
                StatusMessage = "Alert sounds muted. Press the speaker again to turn them back on.";
            }
            else
            {
                PlayCompletionAlert(failed: false);
                StatusMessage = "Alert sounds on.";
            }
        }
    }

    /// <summary>Tooltip for the header speaker button, so its current state is readable.</summary>
    public string AlertSoundToggleHint => IsAlertSoundEnabled
        ? "Alert sounds are on — finished publish, deploy, and file tasks play a sound. Click to mute."
        : "Alert sounds are muted. Click to hear a sound when a task finishes.";

    [RelayCommand]
    private void ToggleAlertSound() => IsAlertSoundEnabled = !IsAlertSoundEnabled;

    private void ResetScreenshotCountdown()
    {
        IsScreenshotCountdownVisible = false;
        ScreenshotCountdownText = string.Empty;
        ScreenshotCountdownProgress = 0;
    }

    private bool CanRunAndroidActions()
    {
        return !OperatingSystem.IsBrowser() && IsAndroidPlatform && !IsBusy;
    }

    private bool TryValidateAndroidDeviceSelection(string actionName)
    {
        if (SelectedDevice is null)
        {
            ReportPreflightFailure(actionName, "Select an Android device first.");
            return false;
        }

        if (!SelectedDevice.IsReady || string.IsNullOrWhiteSpace(SelectedDevice.Serial))
        {
            ReportPreflightFailure(
                actionName,
                SelectedDevice.StateDetail.Length > 0
                    ? $"This target is not ready: {SelectedDevice.StateDetail}."
                    : "Select a running Android device first.");
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

    private void UpdateGooglePlayContext()
    {
        _googlePlayViewModel.UpdateContext(new GooglePlayProjectContext(
            CreateConfiguration(),
            PackageId,
            ProjectDisplayVersion,
            ProjectInternalVersion,
            IsAndroidPlatform));
        UpdateNuGetContext();
    }

    internal PublishConfiguration CreatePublishConfiguration() => CreateConfiguration();

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
            OutputLayout = _projectOutputLayout,
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
            KeyPassword = KeyPassword,
            ProjectName = string.IsNullOrWhiteSpace(SelectedProjectFile)
                ? string.Empty
                : Path.GetFileNameWithoutExtension(SelectedProjectFile)
        };
    }

    private void RefreshCommandPreview()
    {
        // The NuGet platform has no dotnet publish command; the Output preview shows the pack command.
        if (IsNuGetPlatform)
        {
            CommandPreview = string.IsNullOrWhiteSpace(_nugetViewModel.PackCommandPreview)
                ? "Select a project to build the pack command."
                : _nugetViewModel.PackCommandPreview;
            KnownGoodBaselineCommand = string.Empty;
            return;
        }

        try
        {
            var bundle = _publisherService.BuildPublishCommandBundle(CreateConfiguration(), ResolveProjectFilePath());
            SelectedProjectFile = bundle.ProjectFilePath;
            CommandPreview = bundle.PreviewText;
            KnownGoodBaselineCommand = bundle.BaselinePreviewText;
        }
        catch (Exception ex)
        {
            CommandPreview = $"Invalid config: {ex.Message}";
            KnownGoodBaselineCommand = string.Empty;
        }
    }

    private void SetAutomaticOutputDirectory(string outputDirectory)
    {
        _isAssigningAutomaticOutputDirectory = true;
        OutputDirectory = outputDirectory;
        _isAssigningAutomaticOutputDirectory = false;
        _isOutputDirectoryManualOverride = false;
    }

    private void AppendLog(string text)
    {
        lock (_logLock)
        {
            _logBuilder.Append(text);
        }

        ScheduleLogFlush();
    }

    private void ScheduleLogFlush()
    {
        if (!ShouldFlushNow())
        {
            return;
        }

        lock (_logLock)
        {
            if (_logFlushScheduled)
            {
                return;
            }

            _logFlushScheduled = true;
        }

        Dispatcher.UIThread.Post(FlushLog, DispatcherPriority.Background);
    }

    private bool ShouldFlushNow()
    {
        var nowTicks = Environment.TickCount64;
        var previousTicks = Interlocked.Read(ref _lastLogFlushTicks);

        if (nowTicks - previousTicks < LogFlushIntervalMs)
        {
            return false;
        }

        return Interlocked.CompareExchange(ref _lastLogFlushTicks, nowTicks, previousTicks) == previousTicks;
    }

    private void FlushLog()
    {
        string output;
        lock (_logLock)
        {
            _logFlushScheduled = false;
            output = _logBuilder.ToString();
        }

        LiveOutput = output;
    }

    private void FlushLogFinal()
    {
        Interlocked.Exchange(ref _lastLogFlushTicks, Environment.TickCount64);

        string output;
        lock (_logLock)
        {
            _logFlushScheduled = false;
            output = _logBuilder.ToString();
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            LiveOutput = output;
            return;
        }

        Dispatcher.UIThread.Invoke(() => LiveOutput = output);
    }

    private void ClearLog()
    {
        lock (_logLock)
        {
            _logBuilder.Clear();
            _logFlushScheduled = false;
        }

        Interlocked.Exchange(ref _lastLogFlushTicks, 0);
        LiveOutput = string.Empty;
    }

    partial void OnSelfContainedChanged(bool value)
    {
        NormalizePublishSettings(PublishOptionId.SelfContained, value);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsPublishTrimmedEnabled)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(PublishTrimmedDisabledReason)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsPublishAotEnabled)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(PublishAotDisabledReason)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsPublishSingleFileEnabled)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(PublishSingleFileDisabledReason)));
    }

    partial void OnPublishTrimmedChanged(bool value)
    {
        NormalizePublishSettings(PublishOptionId.PublishTrimmed, value);
    }

    partial void OnPublishSingleFileChanged(bool value)
    {
        NormalizePublishSettings(PublishOptionId.PublishSingleFile, value);
    }

    partial void OnPublishAotChanged(bool value)
    {
        NormalizePublishSettings(PublishOptionId.PublishAot, value);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsReadyToRunEnabled)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsReadyToRunCheckBoxEnabled)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsPublishAotEnabled)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(PublishAotDisabledReason)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsPublishTrimmedEnabled)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(PublishTrimmedDisabledReason)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(ReadyToRunDisabledReason)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsPublishSingleFileEnabled)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(PublishSingleFileDisabledReason)));
    }

    partial void OnPublishReadyToRunChanged(bool value)
    {
        NormalizePublishSettings(PublishOptionId.PublishReadyToRun, value);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(ReadyToRunDisabledReason)));
    }

    partial void OnRunAotCompilationChanged(bool value)
    {
        NormalizePublishSettings(PublishOptionId.RunAotCompilation, value);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsProfiledAotEnabled)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(ProfiledAotDisabledReason)));
    }

    partial void OnAndroidLinkModeChanged(string value)
    {
        NormalizePublishSettings(PublishOptionId.AndroidLinkMode, !string.Equals(value, "None", StringComparison.Ordinal));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsShrinkerSettingsEnabled)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsRunAotEnabled)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(RunAotDisabledReason)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsPublishTrimmedEnabled)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(PublishTrimmedDisabledReason)));
    }

    partial void OnIncludeApkChanged(bool value)
    {
        NormalizePublishSettings(PublishOptionId.IncludeApk, value);
    }

    partial void OnIncludeAabChanged(bool value)
    {
        NormalizePublishSettings(PublishOptionId.IncludeAab, value);
    }

    partial void OnEnableProfiledAotChanged(bool value)
    {
        NormalizePublishSettings(PublishOptionId.EnableProfiledAot, value);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsProfiledAotEnabled)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(ProfiledAotDisabledReason)));
    }

    partial void OnUseAppHostChanged(bool value)
    {
        NormalizePublishSettings(PublishOptionId.UseAppHost, value);
    }

    partial void OnCreateMacAppBundleChanged(bool value)
    {
        NormalizePublishSettings(PublishOptionId.CreateMacAppBundle, value);
    }

    partial void OnCreateWindowsExecutableChanged(bool value)
    {
        NormalizePublishSettings(PublishOptionId.CreateWindowsExecutable, value);
    }

    partial void OnBuildIpaChanged(bool value)
    {
        NormalizePublishSettings(PublishOptionId.BuildIpa, value);
    }

    partial void OnArchiveOnBuildChanged(bool value)
    {
        NormalizePublishSettings(PublishOptionId.ArchiveOnBuild, value);
    }

    partial void OnCreateMappingFileChanged(bool value)
    {
        NormalizePublishSettings(PublishOptionId.CreateMappingFile, value);
    }

    partial void OnEnableMultiDexChanged(bool value)
    {
        NormalizePublishSettings(PublishOptionId.EnableMultiDex, value);
    }

    partial void OnUseAapt2Changed(bool value)
    {
        NormalizePublishSettings(PublishOptionId.UseAapt2, value);
    }

    partial void OnEnableDesugarChanged(bool value)
    {
        NormalizePublishSettings(PublishOptionId.EnableDesugar, value);
    }

    public bool IsReadyToRunEnabled => PublishOptionPolicy.IsReadyToRunEnabled(PublishAot, PublishPlatform, TargetFramework, RuntimeIdentifier);
    public bool IsReadyToRunCheckBoxEnabled => IsEditorEnabled && IsReadyToRunEnabled;
}
