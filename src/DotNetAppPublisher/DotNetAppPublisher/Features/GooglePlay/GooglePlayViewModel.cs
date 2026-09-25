using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotNetAppPublisher.Features.GooglePlay.Authentication;
using DotNetAppPublisher.Features.GooglePlay.Models;
using DotNetAppPublisher.Features.GooglePlay.Releases;
using DotNetAppPublisher.Features.GooglePlay.Services;
using DotNetAppPublisher.Features.GooglePlay.Setup;
using DotNetAppPublisher.Features.GooglePlay.Signing;
using DotNetAppPublisher.Features.GooglePlay.State;
using DotNetAppPublisher.Features.GooglePlay.Tracks;
using DotNetAppPublisher.Models;
using DotNetAppPublisher.Services;

namespace DotNetAppPublisher.Features.GooglePlay;

public sealed partial class GooglePlayViewModel : ObservableObject
{
    private readonly PublisherService _publisherService;
    private readonly DesktopInteractionService _desktopInteractionService;
    private readonly GoogleAuthenticationWorkflow _authenticationWorkflow;
    private readonly GooglePlayTrackWorkflow _trackWorkflow;
    private readonly GooglePlayReleaseWorkflow _releaseWorkflow;
    private readonly GooglePlaySigningWorkflow _signingWorkflow;
    private readonly EnvironmentVariableService _environmentVariableService;
    private readonly Action? _openSigning;
    private readonly GooglePlayStateHolder _stateHolder = new();
    private CancellationTokenSource? _operationCts;
    private GooglePlayProjectContext? _context;
    private GooglePlayPublishStage _stage = GooglePlayPublishStage.Idle;
    private GooglePlayStatusTone _statusTone = GooglePlayStatusTone.Info;
    private bool _isProductionSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    private GooglePlayAccountInfo? selectedAccount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    [NotifyPropertyChangedFor(nameof(IsProductionSelected))]
    private GooglePlayTrackInfo? selectedTrack;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    [NotifyPropertyChangedFor(nameof(IsReleaseReady))]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private string releaseName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    [NotifyPropertyChangedFor(nameof(IsReleaseReady))]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private string releaseNotes = string.Empty;

    [ObservableProperty]
    private string releaseNotesLanguage = "en-US";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    [NotifyPropertyChangedFor(nameof(RolloutLabel))]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private int rolloutPercentage = 100;

    [ObservableProperty]
    private string activity = "Connect a Google account to begin.";

    [ObservableProperty]
    private string statusHeadline = "Not connected";

    [ObservableProperty]
    private string statusDetail = "Your credentials stay in memory for this session.";

    [ObservableProperty]
    private bool isSignedIn;

    public ObservableCollection<GooglePlayEnvironmentVariableViewModel> EnvironmentVariables { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSigningKey))]
    [NotifyPropertyChangedFor(nameof(IsReadyToSubmit))]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    private GooglePlaySigningKeyInfo? signingKeyInfo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    [NotifyPropertyChangedFor(nameof(IsReadyToSubmit))]
    private string keystorePath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    [NotifyPropertyChangedFor(nameof(IsReadyToSubmit))]
    private string keyAlias = "upload";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    [NotifyPropertyChangedFor(nameof(IsReadyToSubmit))]
    private string keystorePassword = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    [NotifyPropertyChangedFor(nameof(IsReadyToSubmit))]
    private string keyPassword = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshTracksCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    [NotifyCanExecuteChangedFor(nameof(CheckStatusCommand))]
    private bool isBusy;

    internal GooglePlayViewModel(
        PublisherService publisherService,
        DesktopInteractionService desktopInteractionService,
        GoogleAuthenticationWorkflow authenticationWorkflow,
        GooglePlayTrackWorkflow trackWorkflow,
        GooglePlayReleaseWorkflow releaseWorkflow,
        GooglePlaySigningWorkflow signingWorkflow,
        EnvironmentVariableService environmentVariableService,
        Action? openSigning = null)
    {
        _publisherService = publisherService;
        _desktopInteractionService = desktopInteractionService;
        _authenticationWorkflow = authenticationWorkflow;
        _trackWorkflow = trackWorkflow;
        _releaseWorkflow = releaseWorkflow;
        _signingWorkflow = signingWorkflow;
        _environmentVariableService = environmentVariableService;
        _openSigning = openSigning;
        RefreshEnvironmentVariables();
        RefreshEnvironmentStatus();
    }

    public ObservableCollection<GooglePlayAccountInfo> Accounts { get; } = [];
    public ObservableCollection<GooglePlayTrackInfo> Tracks { get; } = [];

    public string PackageId => _context?.PackageId ?? "No Android package detected";
    public string VersionLabel => _context is null ? "Select an Android project" : $"{_context.DisplayVersion} ({_context.VersionCode})";
    public string OutputDirectory => _context?.PublishConfiguration.OutputDirectory ?? string.Empty;
    public string TrackLabel => SelectedTrack is null
        ? "No track selected"
        : $"{SelectedTrack.DisplayName} · {SelectedTrack.Status}";
    public string OutputDirectoryLabel => string.IsNullOrWhiteSpace(OutputDirectory) ? "No output folder selected" : OutputDirectory;
    public string ReadinessSummary => IsProjectReady
        ? IsSignedIn
            ? SelectedTrack is null ? "Connect an account to discover tracks" : "Ready to submit a release"
            : "Connect your Google Play account"
        : "Select an Android project";
    public string ReadinessHeadline => IsProjectReady
        ? IsSignedIn ? "Google Play connected" : "Connect Google Play"
        : "Android project required";
    public string SecuritySummary => "OAuth tokens and keystore passwords stay in memory. Closing the app clears them.";
    public bool IsDesktopSession => !OperatingSystem.IsBrowser();
    public string DesktopOnlyMessage => IsDesktopSession ? string.Empty : "Google Play sign-in is available in the desktop app.";
    public bool IsBrowser => OperatingSystem.IsBrowser();
    public string PlatformLabel => IsBrowser ? "Browser preview" : "Desktop session";
    public bool IsReadyToSubmit => CanPublish;
    public bool IsProductionRolloutEnabled => SelectedTrack?.IsProduction == true;
    public bool IsTrackReady => SelectedTrack is not null;
    public string TrackStatusLabel => SelectedTrack is null ? "Track status unavailable" : SelectedTrack.Status;
    public string RolloutLabel => IsProductionRolloutEnabled
        ? $"Production rollout ({RolloutPercentage}%)"
        : "Testing track publishes at 100%";
    public string BuildSummary => IsBuildReady
        ? "Signed AAB build is ready to start."
        : "Select an Android project and upload key before building.";
    public bool IsAabReady => HasAab;
    public bool IsBuildReady => IsProjectReady && HasSigningKey;
    public bool HasAab => !OperatingSystem.IsBrowser() &&
        !string.IsNullOrWhiteSpace(OutputDirectory) &&
        Directory.Exists(OutputDirectory) &&
        Directory.EnumerateFiles(OutputDirectory, "*.aab", SearchOption.AllDirectories).Any();
    public bool HasSigningKey => SigningKeyInfo is not null;
    public string ReadinessDetail => HasSigningKey
        ? "Upload key ready. Build and publish uses the selected keystore."
        : "No dedicated upload key is selected. Add one before publishing to Google Play.";
    public string ArtifactValidationSummary => HasAab
        ? "A .aab is present. Structure is checked immediately before upload."
        : "No .aab is present yet. Build and publish creates one.";
    public string ReleaseReadiness => IsProjectReady && IsSignedIn && SelectedAccount is not null
        ? SelectedTrack is null ? "Choose a release track" : IsReleaseReady ? "Release settings are ready" : "Add a release name and notes"
        : "Connect an account and choose a track";
    public bool IsUploadKeyRequired => !HasSigningKey;
    public bool IsProjectReady => _context?.IsAndroidProject == true && !string.IsNullOrWhiteSpace(_context.PackageId);
    public bool CanPublish => IsDesktopSession && IsProjectReady && IsSignedIn && SelectedAccount is not null && SelectedTrack is not null && !IsBusy && !string.IsNullOrWhiteSpace(ReleaseName) && HasSigningKey && !string.IsNullOrWhiteSpace(ReleaseNotes);
    public bool IsReleaseReady => !string.IsNullOrWhiteSpace(ReleaseName) && !string.IsNullOrWhiteSpace(ReleaseNotes);
    public bool IsProductionSelected => _isProductionSelected;
    public GooglePlayStatusTone StatusTone => _statusTone;
    public bool IsStatusSuccess => _statusTone == GooglePlayStatusTone.Success;
    public bool IsStatusWarning => _statusTone == GooglePlayStatusTone.Warning;
    public bool IsStatusError => _statusTone == GooglePlayStatusTone.Error;
    public bool IsStatusInfo => _statusTone is GooglePlayStatusTone.Info or GooglePlayStatusTone.Progress;
    public double ProgressValue => _stage switch
    {
        GooglePlayPublishStage.Building => 20,
        GooglePlayPublishStage.Uploading => 55,
        GooglePlayPublishStage.Submitting => 80,
        GooglePlayPublishStage.InReview => 90,
        GooglePlayPublishStage.Published => 100,
        _ => 0
    };
    public bool IsProgressVisible => IsBusy;
    public GooglePlayPublishStage Stage => _stage;
    public GooglePlaySigningKeyInfo? SigningKey => SigningKeyInfo;

    public string GoogleClientId => _environmentVariableService.GoogleClientId ?? string.Empty;

    public bool IsGoogleClientIdConfigured => EnvironmentVariables
        .FirstOrDefault(variable => variable.Name == "DOTNET_APP_PUBLISHER_GOOGLE_CLIENT_ID")?.IsSet == true;
    public bool IsEnvironmentSetupAvailable => !IsBrowser;
    public bool CanPersistEnvironmentVariables => _environmentVariableService.SupportsPersistentUserVariables;

    public string GoogleClientIdStatus => _environmentVariableService.GoogleClientIdStatus;

    public void RefreshEnvironmentVariables()
    {
        EnvironmentVariables.Clear();
        foreach (var definition in _environmentVariableService.GetKnownVariableDefinitions())
        {
            EnvironmentVariables.Add(new GooglePlayEnvironmentVariableViewModel(
                definition,
                _environmentVariableService,
                RefreshEnvironmentStatus));
        }

        OnPropertyChanged(nameof(EnvironmentVariables));
        OnPropertyChanged(nameof(GoogleClientId));
        OnPropertyChanged(nameof(IsGoogleClientIdConfigured));
        OnPropertyChanged(nameof(IsEnvironmentSetupAvailable));
        OnPropertyChanged(nameof(CanPersistEnvironmentVariables));
        OnPropertyChanged(nameof(GoogleClientIdStatus));
    }

    public void ClearSessionAndForget()
    {
        _operationCts?.Cancel();
        foreach (var account in Accounts.ToArray())
        {
            _authenticationWorkflow.Forget(account.Id);
        }

        Accounts.Clear();
        Tracks.Clear();
        SelectedAccount = null;
        SelectedTrack = null;
        ReleaseNotes = string.Empty;
        _stateHolder.ClearSession();
        KeystorePassword = string.Empty;
        KeyPassword = string.Empty;
        SetStatus("Session cleared", "Google credentials and in-memory key passwords were removed.", GooglePlayStatusTone.Info);
    }

    public void RefreshEnvironmentStatus()
    {
        foreach (var variable in EnvironmentVariables)
        {
            variable.Refresh();
        }

        OnPropertyChanged(nameof(GoogleClientId));
        OnPropertyChanged(nameof(IsGoogleClientIdConfigured));
        OnPropertyChanged(nameof(CanPersistEnvironmentVariables));
        OnPropertyChanged(nameof(GoogleClientIdStatus));
        NotifyCommands();
    }

    public void UpdateContext(GooglePlayProjectContext? context)
    {
        var contextChanged = !string.Equals(_context?.PackageId, context?.PackageId, StringComparison.Ordinal) ||
            !string.Equals(_context?.PublishConfiguration.ProjectDirectory, context?.PublishConfiguration.ProjectDirectory, StringComparison.Ordinal) ||
            !string.Equals(_context?.PublishConfiguration.TargetFramework, context?.PublishConfiguration.TargetFramework, StringComparison.Ordinal) ||
            !string.Equals(_context?.PublishConfiguration.RuntimeIdentifier, context?.PublishConfiguration.RuntimeIdentifier, StringComparison.Ordinal) ||
            !string.Equals(_context?.PublishConfiguration.Configuration, context?.PublishConfiguration.Configuration, StringComparison.Ordinal) ||
            !string.Equals(_context?.PublishConfiguration.OutputDirectory, context?.PublishConfiguration.OutputDirectory, StringComparison.Ordinal);
        _context = context;
        if (contextChanged)
        {
            Tracks.Clear();
            SelectedTrack = null;
            ReleaseName = string.Empty;
            ReleaseNotes = string.Empty;
            ReleaseNotesLanguage = "en-US";
            _stateHolder.ResetRelease();
        }
        OnPropertyChanged(nameof(PackageId));
        OnPropertyChanged(nameof(VersionLabel));
        OnPropertyChanged(nameof(OutputDirectory));
        OnPropertyChanged(nameof(OutputDirectoryLabel));
        OnPropertyChanged(nameof(TrackLabel));
        OnPropertyChanged(nameof(HasAab));
        OnPropertyChanged(nameof(IsAabReady));
        OnPropertyChanged(nameof(IsBuildReady));
        OnPropertyChanged(nameof(BuildSummary));
        OnPropertyChanged(nameof(ArtifactValidationSummary));
        OnPropertyChanged(nameof(ReadinessDetail));
        OnPropertyChanged(nameof(IsUploadKeyRequired));
        OnPropertyChanged(nameof(IsProjectReady));
        OnPropertyChanged(nameof(ReadinessSummary));
        OnPropertyChanged(nameof(ReadinessHeadline));
        OnPropertyChanged(nameof(ReleaseReadiness));
        OnPropertyChanged(nameof(CanPublish));
        OnPropertyChanged(nameof(IsReleaseReady));
        OnPropertyChanged(nameof(IsReadyToSubmit));
        if (context is not null)
        {
            if (contextChanged || string.IsNullOrWhiteSpace(ReleaseName))
            {
                ReleaseName = $"{context.DisplayVersion} ({context.VersionCode})";
            }
            if (contextChanged || _context is not null)
            {
                SetStatus(
                    IsProjectReady ? "Project detected" : "Android target required",
                    IsProjectReady
                        ? "Connect an account to load release tracks."
                        : "Choose an Android project to enable Google Play publishing.",
                    IsProjectReady ? GooglePlayStatusTone.Info : GooglePlayStatusTone.Warning);
            }
        }
    }

    private void SetStatus(string headline, string detail, GooglePlayStatusTone tone)
    {
        _stateHolder.SetStatus(headline, detail, tone);
        StatusHeadline = headline;
        StatusDetail = detail;
        _statusTone = tone;
        OnPropertyChanged(nameof(StatusTone));
        OnPropertyChanged(nameof(IsStatusSuccess));
        OnPropertyChanged(nameof(IsStatusWarning));
        OnPropertyChanged(nameof(IsStatusError));
        OnPropertyChanged(nameof(IsStatusInfo));
    }

    private void SetStage(GooglePlayPublishStage stage)
    {
        _stage = stage;
        _stateHolder.SetStage(stage);
        OnPropertyChanged(nameof(Stage));
        OnPropertyChanged(nameof(ProgressValue));
    }

    [RelayCommand(CanExecute = nameof(CanClearSession))]
    private void ClearSession()
    {
        ClearSessionCore();
    }

    private void ClearSessionCore()
    {
        ClearSessionAndForget();
    }

    [RelayCommand]
    private void OpenGoogleCloudCredentials()
    {
        _environmentVariableService.OpenGoogleCloudCredentials();
    }

    [RelayCommand]
    private void OpenEnvironmentHelp()
    {
        _environmentVariableService.OpenEnvironmentVariableHelp();
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        var googleClientIdVariable = EnvironmentVariables.FirstOrDefault(variable => variable.Name == "DOTNET_APP_PUBLISHER_GOOGLE_CLIENT_ID");
        if (googleClientIdVariable is null || !googleClientIdVariable.IsSet)
        {
            SetStatus(
                "OAuth setup required",
                "Add a public Google Desktop OAuth client ID in Setup before connecting an account.",
                GooglePlayStatusTone.Warning);
            await _desktopInteractionService.ShowErrorAsync(
                "Google Play connection",
                "Add a public Google Desktop OAuth client ID in the Setup card, then try again.");
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var account = await _authenticationWorkflow.SignInAsync(cancellationToken);
            UpsertAccount(account);
            SelectedAccount = account;
            IsSignedIn = true;
            await LoadTracksAsync(cancellationToken);
        }, "Connecting Google account");
    }

    private bool CanConnect() => !IsBusy && IsDesktopSession && IsGoogleClientIdConfigured;

    [RelayCommand(CanExecute = nameof(CanRefreshTracks))]
    private async Task RefreshTracksAsync()
    {
        await RunAsync(LoadTracksAsync, "Loading Google Play tracks");
    }

    private bool CanRefreshTracks() => IsDesktopSession && !IsBusy && IsProjectReady && IsSignedIn && SelectedAccount is not null;

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private async Task DisconnectAsync()
    {
        if (SelectedAccount is null)
        {
            return;
        }

        var accountId = SelectedAccount.Id;
        try
        {
            await _authenticationWorkflow.RevokeAsync(accountId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            SetStatus("Could not revoke Google token", ex.Message, GooglePlayStatusTone.Error);
        }
        finally
        {
            Accounts.Remove(SelectedAccount);
            SelectedAccount = Accounts.FirstOrDefault();
            Tracks.Clear();
            IsSignedIn = SelectedAccount is not null;
        }
    }

    private bool CanDisconnect() => !IsBusy && SelectedAccount is not null;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _operationCts?.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanPublish))]
    private async Task PublishAsync()
    {
        if (_context is null || SelectedAccount is null || SelectedTrack is null)
        {
            return;
        }

        if (!IsProjectReady)
        {
            SetStatus(
                "Android project required",
                "Select an Android project before opening Google Play.",
                GooglePlayStatusTone.Warning);
            return;
        }

        if (!HasSigningKey)
        {
            SetStatus(
                "Upload key required",
                "Create or inspect a dedicated Android upload keystore before publishing.",
                GooglePlayStatusTone.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(ReleaseName))
        {
            SetStatus("Release name required", "Enter a name for this release before publishing.", GooglePlayStatusTone.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(ReleaseNotes))
        {
            SetStatus("Release notes required", "Add release notes before submitting to Google Play.", GooglePlayStatusTone.Warning);
            return;
        }

        var snapshot = CreatePublishSnapshot();
        if (snapshot.Track.IsProduction)
        {
            var confirmed = await _desktopInteractionService.ConfirmAsync(
                "Confirm Google Play release",
                $"Release: {snapshot.ReleaseName}\nNotes language: {snapshot.NotesLanguage}\nTrack: {snapshot.Track.DisplayName}\nPackage: {snapshot.PackageId}\nVersion: {snapshot.VersionLabel}\nRollout: {snapshot.RolloutPercentage}%\n\nProduction publishing can make this release available after Google review. Continue?");
            if (!confirmed)
            {
                return;
            }
        }

        await RunAsync(async cancellationToken =>
        {
            var configuration = CreateStoreConfiguration(snapshot);
            SetStage(GooglePlayPublishStage.Building);
            Activity = "Building a signed Android App Bundle";
            var buildSucceeded = await _publisherService.PublishAsync(
                configuration,
                message =>
                {
                    Activity = SummarizeOutput(message);
                },
                cancellationToken);
            if (!buildSucceeded)
            {
                throw new InvalidOperationException("The Android App Bundle build failed. Open the Log page for the toolchain error.");
            }

            var aab = _publisherService.FindBestAab(
                configuration.OutputDirectory,
                configuration.ProjectName,
                configuration.PackageId)
                ?? throw new FileNotFoundException("The build completed, but no .aab file was found in the output folder.");
            var artifact = await _signingWorkflow.ValidateAsync(aab.FullName, cancellationToken);
            if (!artifact.IsValid)
            {
                throw new InvalidOperationException(artifact.Message);
            }

            SetStage(GooglePlayPublishStage.Uploading);
            var result = await _releaseWorkflow.PublishAsync(
                snapshot.AccountId,
                snapshot.PackageId,
                aab.FullName,
                snapshot.Track.Id,
                snapshot.ReleaseName,
                [new GooglePlayReleaseNote(snapshot.NotesLanguage, snapshot.Notes)],
                snapshot.RolloutPercentage,
                message => Activity = message,
                cancellationToken);
            SetStage(result.Stage);
            SetStatus(
                result.Stage == GooglePlayPublishStage.Published ? "Published" : "In review",
                result.Message,
                result.Stage == GooglePlayPublishStage.Published ? GooglePlayStatusTone.Success : GooglePlayStatusTone.Progress);
        }, "Publishing to Google Play");
    }

    [RelayCommand(CanExecute = nameof(CanCheckStatus))]
    private async Task CheckStatusAsync()
    {
        if (_context is null || SelectedAccount is null || SelectedTrack is null)
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var state = await _releaseWorkflow.GetReleaseStateAsync(
                SelectedAccount.Id,
                _context.PackageId,
                SelectedTrack.Id,
                ReleaseName,
                cancellationToken);
            var resolvedStage = state switch
            {
                "RELEASE_LIFECYCLE_STATE_PUBLISHED" => GooglePlayPublishStage.Published,
                "RELEASE_LIFECYCLE_STATE_IN_REVIEW" => GooglePlayPublishStage.InReview,
                "RELEASE_LIFECYCLE_STATE_NOT_APPROVED" => GooglePlayPublishStage.Failed,
                _ => _stage
            };
            SetStage(resolvedStage);
            StatusHeadline = state switch
            {
                "RELEASE_LIFECYCLE_STATE_PUBLISHED" => "Published",
                "RELEASE_LIFECYCLE_STATE_IN_REVIEW" => "In review",
                "RELEASE_LIFECYCLE_STATE_NOT_APPROVED" => "Rejected",
                "RELEASE_LIFECYCLE_STATE_APPROVED_NOT_PUBLISHED" => "Approved, waiting to publish",
                _ => state.Replace("RELEASE_LIFECYCLE_STATE_", string.Empty).ToLowerInvariant()
            };
            SetStatus(
                StatusHeadline,
                StatusHeadline == "Rejected"
                    ? "Google Play rejected the release. Open Play Console to view the policy reason."
                    : $"Current Google Play state: {state}",
                StatusHeadline == "Rejected" ? GooglePlayStatusTone.Error :
                StatusHeadline == "Published" ? GooglePlayStatusTone.Success :
                StatusHeadline == "In review" ? GooglePlayStatusTone.Progress : GooglePlayStatusTone.Warning);
        }, "Checking Google Play status");
    }

    private bool CanCheckStatus() => IsDesktopSession && !IsBusy && IsProjectReady && IsSignedIn && SelectedAccount is not null && SelectedTrack is not null;

    [RelayCommand(CanExecute = nameof(CanChooseKeystore))]
    private async Task ChooseKeystoreAsync()
    {
        var path = await _desktopInteractionService.PickFileAsync(
            "Choose Android upload keystore",
            _context?.PublishConfiguration.OutputDirectory,
            [new Avalonia.Platform.Storage.FilePickerFileType("Android keystore") { Patterns = ["*.jks", "*.keystore", "*.p12", "*.pfx"] }]);
        if (path is not null)
        {
            KeystorePath = path;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreateSigningKey))]
    private async Task CreateSigningKeyAsync()
    {
        if (string.IsNullOrWhiteSpace(KeystorePath))
        {
            await ChooseKeystoreAsync();
        }
        if (string.IsNullOrWhiteSpace(KeystorePath))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            if (string.IsNullOrWhiteSpace(KeystorePassword))
            {
                KeystorePassword = _signingWorkflow.CreateStrongPassword();
            }

            KeyPassword = KeystorePassword;
            SigningKeyInfo = await _signingWorkflow.CreateAsync(
                KeystorePath,
                KeyAlias,
                KeystorePassword,
                KeystorePassword,
                _context?.PublishConfiguration.ProjectName ?? ".NET App Publisher",
                cancellationToken);
            SetStatus("Signing key ready", "The key and its passwords are held in memory for this session.", GooglePlayStatusTone.Success);
        }, "Creating Android upload key");
    }

    [RelayCommand(CanExecute = nameof(CanInspectSigningKey))]
    private async Task InspectSigningKeyAsync()
    {
        if (string.IsNullOrWhiteSpace(KeystorePath) || string.IsNullOrWhiteSpace(KeystorePassword))
        {
            SetStatus(
                "Signing key needs a password",
                "Choose the keystore and enter its current store password to inspect it.",
                GooglePlayStatusTone.Warning);
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            SigningKeyInfo = await _signingWorkflow.InspectAsync(
                KeystorePath,
                KeyAlias,
                KeystorePassword,
                cancellationToken);
            SetStatus("Signing key verified", $"SHA-256: {SigningKeyInfo.Sha256Fingerprint}", GooglePlayStatusTone.Success);
        }, "Inspecting Android signing key");
    }

    [RelayCommand(CanExecute = nameof(CanClearSigningKey))]
    private void ClearSigningKey()
    {
        SigningKeyInfo = null;
        KeystorePassword = string.Empty;
        KeyPassword = string.Empty;
        SetStatus("Signing key cleared from memory", "The keystore file itself was not changed.", GooglePlayStatusTone.Info);
    }

    [RelayCommand]
    private void OpenSigning()
    {
        if (_openSigning is null)
        {
            SetStatus("Open Signing", "Use the Signing section to configure the Android upload key.", GooglePlayStatusTone.Info);
            return;
        }

        _openSigning();
    }

    [RelayCommand]
    private async Task OpenPlayConsoleAsync()
    {
        if (!IsDesktopSession)
        {
            SetStatus("Desktop app required", "Open Play Console from the desktop app.", GooglePlayStatusTone.Warning);
            return;
        }

        var url = "https://play.google.com/console";
        if (OperatingSystem.IsMacOS())
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("open", url) { UseShellExecute = true });
        }
        else if (OperatingSystem.IsWindows())
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        else
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("xdg-open", url) { UseShellExecute = true });
        }

        await Task.CompletedTask;
    }

    private async Task LoadTracksAsync(CancellationToken cancellationToken)
    {
        if (_context is null || SelectedAccount is null || !IsProjectReady)
        {
            return;
        }

        Activity = "Discovering release tracks";
        var tracks = await _trackWorkflow.GetTracksAsync(SelectedAccount.Id, _context.PackageId, cancellationToken);
        Tracks.Clear();
        foreach (var track in tracks)
        {
            Tracks.Add(track);
        }

        if (Tracks.Count == 0)
        {
            Tracks.Add(new GooglePlayTrackInfo("qa", "Internal testing", "Internal testing", "Not configured", false));
            Tracks.Add(new GooglePlayTrackInfo("beta", "Open testing", "Open testing", "Not configured", false));
            Tracks.Add(new GooglePlayTrackInfo("production", "Production", "Production", "Not configured", true));
        }

        SelectedTrack = Tracks.FirstOrDefault(track => track.Id == "qa")
            ?? Tracks.FirstOrDefault(track => !track.IsProduction)
            ?? Tracks.FirstOrDefault();
        SetStatus(
            tracks.Count == 0 ? "Play Console setup needed" : "Ready to publish",
            tracks.Count == 0
                ? "Standard tracks are shown; confirm access and configuration in Play Console."
                : $"Found {tracks.Count} release track{(tracks.Count == 1 ? string.Empty : "s")}.",
            tracks.Count == 0 ? GooglePlayStatusTone.Warning : GooglePlayStatusTone.Success);
    }

    private async Task RunAsync(Func<CancellationToken, Task> operation, string action)
    {
        if (IsBusy)
        {
            return;
        }

        if (!IsDesktopSession)
        {
            SetStatus("Desktop app required", "Google Play publishing is available in the desktop app.", GooglePlayStatusTone.Warning);
            return;
        }

        IsBusy = true;
        _operationCts = new CancellationTokenSource();
        NotifyCommands();
        try
        {
            await operation(_operationCts.Token);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Cancelled", $"{action} was cancelled.", GooglePlayStatusTone.Info);
        }
        catch (Exception ex)
        {
            SetStage(GooglePlayPublishStage.Failed);
            SetStatus("Needs attention", ex.Message, GooglePlayStatusTone.Error);
            Activity = ex.Message;
        }
        finally
        {
            _operationCts?.Dispose();
            _operationCts = null;
            IsBusy = false;
            NotifyCommands();
            OnPropertyChanged(nameof(Stage));
            OnPropertyChanged(nameof(ProgressValue));
            OnPropertyChanged(nameof(IsProgressVisible));
            OnPropertyChanged(nameof(StatusHeadline));
            OnPropertyChanged(nameof(StatusDetail));
            OnPropertyChanged(nameof(Activity));
            OnPropertyChanged(nameof(StatusTone));
            OnPropertyChanged(nameof(IsStatusSuccess));
            OnPropertyChanged(nameof(IsStatusWarning));
            OnPropertyChanged(nameof(IsStatusError));
            OnPropertyChanged(nameof(IsStatusInfo));
        }
    }

    private bool CanCancel() => IsBusy;

    private bool CanClearSession() => !IsBusy;

    private bool CanClearSigningKey() => !IsBusy && SigningKeyInfo is not null;

    private bool CanChooseKeystore() => IsDesktopSession && !IsBusy;

    private bool CanCreateSigningKey() => IsDesktopSession && !IsBusy;

    private bool CanInspectSigningKey() => IsDesktopSession && !IsBusy;

    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(ReadinessSummary));
        OnPropertyChanged(nameof(CanPublish));
        OnPropertyChanged(nameof(IsReadyToSubmit));
        ConnectCommand.NotifyCanExecuteChanged();
        ClearSessionCommand.NotifyCanExecuteChanged();
        ClearSigningKeyCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(GoogleClientId));
        OnPropertyChanged(nameof(IsGoogleClientIdConfigured));
        OnPropertyChanged(nameof(IsEnvironmentSetupAvailable));
        OnPropertyChanged(nameof(CanPersistEnvironmentVariables));
        OnPropertyChanged(nameof(GoogleClientIdStatus));
        OnPropertyChanged(nameof(EnvironmentVariables));
        CreateSigningKeyCommand.NotifyCanExecuteChanged();
        InspectSigningKeyCommand.NotifyCanExecuteChanged();
        ChooseKeystoreCommand.NotifyCanExecuteChanged();
        RefreshTracksCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
        PublishCommand.NotifyCanExecuteChanged();
        CheckStatusCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private void UpsertAccount(GooglePlayAccountInfo account)
    {
        var existing = Accounts.FirstOrDefault(item => item.Id == account.Id);
        if (existing is not null)
        {
            Accounts.Remove(existing);
        }
        Accounts.Add(account);
    }

    private PublishConfiguration CreateStoreConfiguration(GooglePlayPublishSnapshot snapshot)
    {
        return snapshot.Configuration with
        {
            IncludeApk = false,
            IncludeAab = true,
            Configuration = "Release",
            SignMode = "Sign",
            KeystorePath = snapshot.KeystorePath,
            KeyAlias = snapshot.KeyAlias,
            KeystorePassword = snapshot.KeystorePassword,
            KeyPassword = snapshot.KeyPassword
        };
    }

    private GooglePlayPublishSnapshot CreatePublishSnapshot()
    {
        var context = _context ?? throw new InvalidOperationException("No Android project context is available.");
        var state = _stateHolder.Current;
        var account = state.Account ?? throw new InvalidOperationException("No Google account is selected.");
        var track = state.Track ?? throw new InvalidOperationException("No Google Play track is selected.");
        var signingKey = SigningKey ?? throw new InvalidOperationException("No Android upload key is selected.");
        return new GooglePlayPublishSnapshot(
            account.Id,
            track,
            context.PackageId,
            context.PublishConfiguration,
            state.ReleaseName,
            state.ReleaseNotesLanguage,
            state.ReleaseNotes,
            state.RolloutPercentage,
            state.SigningKeyPath,
            state.SigningKeyAlias,
            KeystorePassword,
            KeyPassword,
            $"{context.DisplayVersion} ({context.VersionCode})");
    }

    private static string SummarizeOutput(string output)
    {
        var line = output.Trim();
        if (line.Length == 0)
        {
            return "Building Android App Bundle";
        }

        return line.Length <= 120 ? line : line[..117] + "...";
    }

    private sealed record GooglePlayPublishSnapshot(
        string AccountId,
        GooglePlayTrackInfo Track,
        string PackageId,
        PublishConfiguration Configuration,
        string ReleaseName,
        string NotesLanguage,
        string Notes,
        int RolloutPercentage,
        string KeystorePath,
        string KeyAlias,
        string KeystorePassword,
        string KeyPassword,
        string VersionLabel);

    partial void OnSigningKeyInfoChanged(GooglePlaySigningKeyInfo? value)
    {
        _stateHolder.SetSigningKey(value?.Path ?? string.Empty, value?.Alias ?? string.Empty);
    }

    partial void OnReleaseNameChanged(string value)
    {
        _stateHolder.SetRelease(value, ReleaseNotes, ReleaseNotesLanguage, RolloutPercentage);
    }

    partial void OnReleaseNotesChanged(string value)
    {
        _stateHolder.SetRelease(ReleaseName, value, ReleaseNotesLanguage, RolloutPercentage);
    }

    partial void OnReleaseNotesLanguageChanged(string value)
    {
        _stateHolder.SetRelease(ReleaseName, ReleaseNotes, value, RolloutPercentage);
    }

    partial void OnRolloutPercentageChanged(int value)
    {
        _stateHolder.SetRelease(ReleaseName, ReleaseNotes, ReleaseNotesLanguage, value);
    }

    partial void OnKeystorePathChanged(string value)
    {
        SigningKeyInfo = null;
    }

    partial void OnKeyAliasChanged(string value)
    {
        SigningKeyInfo = null;
    }

    partial void OnKeystorePasswordChanged(string value)
    {
        SigningKeyInfo = null;
    }

    partial void OnKeyPasswordChanged(string value)
    {
        SigningKeyInfo = null;
    }

    partial void OnSelectedAccountChanged(GooglePlayAccountInfo? value)
    {
        _stateHolder.SetAccount(value);
        IsSignedIn = value is not null;
        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(ReadinessSummary));
        OnPropertyChanged(nameof(CanPublish));
        OnPropertyChanged(nameof(IsReadyToSubmit));
        OnPropertyChanged(nameof(HasSigningKey));
        OnPropertyChanged(nameof(IsUploadKeyRequired));
        NotifyCommands();
    }

    partial void OnSelectedTrackChanged(GooglePlayTrackInfo? value)
    {
        _isProductionSelected = value?.IsProduction == true;
        if (value?.IsProduction != true)
        {
            RolloutPercentage = 100;
        }
        else if (RolloutPercentage is < 1 or > 100)
        {
            RolloutPercentage = 100;
        }
        _stateHolder.SetTrack(value, RolloutPercentage);
        OnPropertyChanged(nameof(IsProductionSelected));
        OnPropertyChanged(nameof(IsProductionRolloutEnabled));
        OnPropertyChanged(nameof(IsTrackReady));
        OnPropertyChanged(nameof(TrackStatusLabel));
        OnPropertyChanged(nameof(RolloutLabel));
        OnPropertyChanged(nameof(TrackLabel));
        OnPropertyChanged(nameof(ReadinessSummary));
        OnPropertyChanged(nameof(CanPublish));
        OnPropertyChanged(nameof(IsReadyToSubmit));
        OnPropertyChanged(nameof(HasSigningKey));
        OnPropertyChanged(nameof(IsUploadKeyRequired));
        NotifyCommands();
    }

}
