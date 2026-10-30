using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CodePublisher.Features.NuGet.Build;
using CodePublisher.Features.NuGet.Feeds;
using CodePublisher.Features.NuGet.Models;
using CodePublisher.Features.NuGet.Services;
using CodePublisher.Services;

namespace CodePublisher.Features.NuGet;

/// <summary>
/// Backs the NuGet tab and the NuGet deploy card: pack the selected project into a .nupkg, validate
/// and test-install it, then push it to NuGet.org or GitHub Packages and manage the versions there.
/// The API key lives only in this session's memory and is masked in every command preview.
/// </summary>
public sealed partial class NuGetViewModel : ObservableObject
{
    private readonly PublisherService _publisherService;
    private readonly DesktopInteractionService _interaction;
    private readonly NuGetPackageService _service;
    private readonly Action<string> _writeLog;
    private CancellationTokenSource? _cts;
    private string _projectDirectory = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProject))]
    [NotifyCanExecuteChangedFor(nameof(PackCommand))]
    private string _projectFile = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PackCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshVersionsCommand))]
    [NotifyPropertyChangedFor(nameof(PackCommandPreview))]
    private string _packageId = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PackCommandPreview))]
    private string _packageVersion = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PackCommandPreview))]
    private string _configuration = "Release";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PackCommandPreview))]
    private string _outputDirectory = string.Empty;

    [ObservableProperty]
    private bool _includeSymbols;

    [ObservableProperty]
    private string _packCommandPreview = "Select a project to build the pack command.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PackCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestPackageCommand))]
    [NotifyCanExecuteChangedFor(nameof(PushCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Pack the selected project into a NuGet package, test it, then push it to a feed.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPackedPackage))]
    [NotifyCanExecuteChangedFor(nameof(TestPackageCommand))]
    [NotifyCanExecuteChangedFor(nameof(PushCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenPackageFolderCommand))]
    private string _packedPackagePath = string.Empty;

    [ObservableProperty]
    private string _packedPackageLabel = string.Empty;

    [ObservableProperty]
    private string _validationSummary = string.Empty;

    public ObservableCollection<NuGetValidationIssue> ValidationIssues { get; } = [];

    public bool HasProject => !string.IsNullOrWhiteSpace(ProjectFile);

    public bool HasPackedPackage => !string.IsNullOrWhiteSpace(PackedPackagePath);

    public bool HasValidationIssues => ValidationIssues.Count > 0;

    // Deploy to a feed.
    public IReadOnlyList<NuGetFeedTarget> FeedTargets => NuGetFeedCatalog.Targets;

    public IReadOnlyList<string> ConfigurationOptions { get; } = ["Release", "Debug"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FeedHint))]
    [NotifyPropertyChangedFor(nameof(FeedOwnerLabel))]
    [NotifyPropertyChangedFor(nameof(IsFeedOwnerVisible))]
    [NotifyPropertyChangedFor(nameof(IsApiKeyVisible))]
    [NotifyCanExecuteChangedFor(nameof(PushCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshVersionsCommand))]
    private NuGetFeedTarget _selectedFeed = NuGetFeedCatalog.DefaultTarget;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FeedHint))]
    [NotifyCanExecuteChangedFor(nameof(PushCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshVersionsCommand))]
    private string _feedOwnerOrFolder = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PushCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshVersionsCommand))]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshVersionsCommand))]
    private bool _isFeedBusy;

    [ObservableProperty]
    private string _feedStatus = "Choose a feed, then refresh to see the versions already hosted there.";

    public ObservableCollection<NuGetFeedVersionRowViewModel> FeedVersions { get; } = [];

    public bool HasFeedVersions => FeedVersions.Count > 0;

    public bool IsDesktopSession => !OperatingSystem.IsBrowser();

    public bool IsBrowser => OperatingSystem.IsBrowser();

    public bool IsFeedOwnerVisible => SelectedFeed.RequiresOwner || SelectedFeed.Kind == NuGetFeedKind.LocalFolder;

    public bool IsApiKeyVisible => SelectedFeed.RequiresApiKey;

    public string FeedOwnerLabel => SelectedFeed.Kind == NuGetFeedKind.LocalFolder ? "Local folder" : "GitHub owner";

    public string FeedHint => SelectedFeed.Kind switch
    {
        NuGetFeedKind.GitHubPackages => "GitHub username or organization that owns the package, for example omoi or my-org.",
        NuGetFeedKind.LocalFolder => "A folder on this computer, for example the pack output folder. The package is copied there.",
        _ => "No owner needed for nuget.org."
    };

    internal NuGetViewModel(
        PublisherService publisherService,
        DesktopInteractionService interaction,
        NuGetPackageService service,
        Action<string> writeLog)
    {
        _publisherService = publisherService;
        _interaction = interaction;
        _service = service;
        _writeLog = writeLog;
    }

    /// <summary>
    /// Runs the pack pipeline. The header Publish button calls this when the NuGet platform is
    /// selected, so "Publish" creates the .nupkg.
    /// </summary>
    internal Task RunPackAsync() => PackCommand.ExecuteAsync(null);

    /// <summary>Called when the project selection or its metadata changes in the main view model.</summary>
    public void UpdateContext(string projectDirectory, string projectFile, string packageId, string version)
    {
        _projectDirectory = projectDirectory ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(projectFile))
        {
            ProjectFile = projectFile;
        }

        if (!string.IsNullOrWhiteSpace(packageId))
        {
            PackageId = packageId;
        }

        if (!string.IsNullOrWhiteSpace(version))
        {
            PackageVersion = version;
        }

        if (string.IsNullOrWhiteSpace(OutputDirectory) && !string.IsNullOrWhiteSpace(_projectDirectory))
        {
            OutputDirectory = Path.Combine(_projectDirectory, "nupkgs");
        }

        RefreshPackPreview();
        TryAutoDetectOwner();
    }

    partial void OnSelectedFeedChanged(NuGetFeedTarget value)
    {
        TryAutoDetectOwner();
        if (value.RequiresApiKey && string.IsNullOrWhiteSpace(ApiKey))
        {
            _ = DetectApiKeySilentlyAsync(value);
        }
    }

    /// <summary>Reads the GitHub owner from the csproj repository URL or the nearest git remote, when not set yet.</summary>
    private void TryAutoDetectOwner()
    {
        if (SelectedFeed.Kind != NuGetFeedKind.GitHubPackages || !string.IsNullOrWhiteSpace(FeedOwnerOrFolder))
        {
            return;
        }

        var owner = NuGetPackageService.DetectGitHubOwner(ProjectFile, _projectDirectory);
        if (owner is not null)
        {
            FeedOwnerOrFolder = owner;
            FeedStatus = $"GitHub owner '{owner}' detected from the project's repository URL or git remote.";
        }
    }

    private async Task DetectApiKeySilentlyAsync(NuGetFeedTarget target)
    {
        try
        {
            var detected = await _service.DetectApiKeyAsync(target.Kind, CancellationToken.None);
            if (detected is not null && string.IsNullOrWhiteSpace(ApiKey))
            {
                ApiKey = detected.Token;
                FeedStatus = $"API key found via {detected.Source}; kept in memory for this session only.";
            }
        }
        catch
        {
            // Detection is best-effort; the user can still paste a key.
        }
    }

    [RelayCommand]
    private async Task DetectCredentialsAsync()
    {
        TryAutoDetectOwner();
        if (!SelectedFeed.RequiresApiKey)
        {
            FeedStatus = "This feed needs no API key.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            FeedStatus = "An API key is already set for this session.";
            return;
        }

        IsFeedBusy = true;
        try
        {
            var detected = await _service.DetectApiKeyAsync(SelectedFeed.Kind, CancellationToken.None);
            if (detected is not null)
            {
                ApiKey = detected.Token;
                FeedStatus = $"API key found via {detected.Source}; kept in memory for this session only.";
            }
            else
            {
                FeedStatus = SelectedFeed.Kind == NuGetFeedKind.GitHubPackages
                    ? "No key found. Set GH_TOKEN or GITHUB_TOKEN, or run `gh auth login`, then press Detect again."
                    : "No key found. Set NUGET_API_KEY to your nuget.org API key, then press Detect again.";
            }
        }
        finally
        {
            IsFeedBusy = false;
        }
    }

    partial void OnProjectFileChanged(string value) => RefreshPackPreview();

    partial void OnConfigurationChanged(string value) => RefreshPackPreview();

    partial void OnOutputDirectoryChanged(string value) => RefreshPackPreview();

    private void RefreshPackPreview()
    {
        if (string.IsNullOrWhiteSpace(ProjectFile))
        {
            PackCommandPreview = "Select a project to build the pack command.";
            return;
        }

        try
        {
            var dotnet = _publisherService.DotnetPath ?? "dotnet";
            var arguments = NuGetCommandBuilder.BuildPackArguments(
                dotnet,
                ProjectFile,
                string.IsNullOrWhiteSpace(Configuration) ? "Release" : Configuration,
                string.IsNullOrWhiteSpace(OutputDirectory) ? Path.Combine(_projectDirectory, "nupkgs") : OutputDirectory,
                PackageVersion,
                IncludeSymbols);
            PackCommandPreview = NuGetCommandBuilder.Mask(arguments);
        }
        catch (Exception exception)
        {
            PackCommandPreview = $"Invalid pack configuration: {exception.Message}";
        }
    }

    private bool CanPack() => IsDesktopSession && !IsBusy && HasProject;

    private bool CanTestPackage() => IsDesktopSession && !IsBusy && HasPackedPackage;

    private bool CanPush() =>
        IsDesktopSession && !IsBusy && HasPackedPackage && CanUseFeed(requireApiKey: true);

    private bool CanRefreshVersions() =>
        IsDesktopSession && !IsFeedBusy && NuGetFeedCatalog.ValidatePackageId(PackageId) is null
        // nuget.org lists anonymously; GitHub Packages needs a PAT with read:packages even for public packages.
        && CanUseFeed(requireApiKey: SelectedFeed.Kind == NuGetFeedKind.GitHubPackages);

    private bool CanUseFeed(bool requireApiKey) =>
        (!requireApiKey || !SelectedFeed.RequiresApiKey || !string.IsNullOrWhiteSpace(ApiKey))
        && (!SelectedFeed.RequiresOwner && SelectedFeed.Kind != NuGetFeedKind.LocalFolder
            || !string.IsNullOrWhiteSpace(FeedOwnerOrFolder));

    [RelayCommand(CanExecute = nameof(CanPack))]
    private async Task PackAsync()
    {
        IsBusy = true;
        try
        {
            _cts = new CancellationTokenSource();
            var versionError = NuGetFeedCatalog.ValidateVersion(PackageVersion);
            if (versionError is not null)
            {
                StatusMessage = versionError;
                return;
            }

            var outcome = await Task.Run(
                () => _service.PackAsync(
                    ProjectFile,
                    string.IsNullOrWhiteSpace(Configuration) ? "Release" : Configuration,
                    string.IsNullOrWhiteSpace(OutputDirectory) ? Path.Combine(_projectDirectory, "nupkgs") : OutputDirectory,
                    string.IsNullOrWhiteSpace(PackageVersion) ? null : PackageVersion,
                    IncludeSymbols,
                    _writeLog,
                    _cts.Token),
                _cts.Token);

            ApplyPackOutcome(outcome);
            StatusMessage = outcome.Message;
            _publisherService.AlertSounds.Play(outcome.Success ? AlertTone.Success : AlertTone.Failure);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Pack cancelled.";
        }
        catch (Exception exception)
        {
            StatusMessage = "Pack could not start.";
            await _interaction.ShowErrorAsync("NuGet pack failed", exception.Message);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsBusy = false;
        }
    }

    private void ApplyPackOutcome(NuGetPackOutcome outcome)
    {
        ValidationIssues.Clear();
        if (outcome.Package is null)
        {
            PackedPackagePath = string.Empty;
            PackedPackageLabel = string.Empty;
            ValidationSummary = string.Empty;
            return;
        }

        PackedPackagePath = outcome.Package.PackagePath;
        PackedPackageLabel = Path.GetFileName(outcome.Package.PackagePath);
        ValidationSummary = outcome.Package.Validation.Summary;
        foreach (var issue in outcome.Package.Validation.Issues)
        {
            ValidationIssues.Add(issue);
        }
    }

    [RelayCommand(CanExecute = nameof(CanTestPackage))]
    private async Task TestPackageAsync()
    {
        if (!TryGetPackedIdentity(out var identity, out var folder))
        {
            return;
        }

        IsBusy = true;
        try
        {
            _cts = new CancellationTokenSource();
            var result = await Task.Run(
                () => _service.TestPackageAsync(identity!, folder!, _writeLog, _cts.Token),
                _cts.Token);
            StatusMessage = result.Message;
            _publisherService.AlertSounds.Play(result.Success ? AlertTone.Success : AlertTone.Failure);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Package test cancelled.";
        }
        catch (Exception exception)
        {
            StatusMessage = "Package test could not start.";
            await _interaction.ShowErrorAsync("NuGet package test failed", exception.Message);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanPush))]
    private async Task PushAsync()
    {
        if (!TryGetPackedIdentity(out var identity, out var folder))
        {
            return;
        }

        IsBusy = true;
        try
        {
            _cts = new CancellationTokenSource();
            var result = await Task.Run(
                () => _service.PushAsync(
                    PackedPackagePath,
                    SelectedFeed,
                    FeedOwnerOrFolder,
                    ApiKey,
                    _writeLog,
                    _cts.Token),
                _cts.Token);
            StatusMessage = result.Message;
            FeedStatus = result.Message;
            _publisherService.AlertSounds.Play(result.Success ? AlertTone.Success : AlertTone.Failure);
            if (result.Success)
            {
                await RefreshVersionsAsync();
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Push cancelled.";
        }
        catch (Exception exception)
        {
            StatusMessage = "Push could not start.";
            await _interaction.ShowErrorAsync("NuGet push failed", exception.Message);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefreshVersions))]
    private async Task RefreshVersionsAsync()
    {
        IsFeedBusy = true;
        try
        {
            var versions = await _service.ListVersionsAsync(SelectedFeed, FeedOwnerOrFolder, PackageId, ApiKey, CancellationToken.None)
                ;
            FeedVersions.Clear();
            foreach (var version in versions)
            {
                FeedVersions.Add(new NuGetFeedVersionRowViewModel(version, RequestDeleteVersionAsync));
            }

            OnPropertyChanged(nameof(HasFeedVersions));
            FeedStatus = versions.Count == 0
                ? $"No versions of {PackageId} on {SelectedFeed.Label} yet."
                : $"{versions.Count} version(s) of {PackageId} on {SelectedFeed.Label}.";
        }
        catch (Exception exception)
        {
            FeedStatus = $"Could not list versions: {exception.Message}";
        }
        finally
        {
            IsFeedBusy = false;
        }
    }

    private async void RequestDeleteVersionAsync(NuGetFeedVersionRowViewModel row)
    {
        if (IsFeedBusy || IsBusy)
        {
            return;
        }

        var confirmed = await _interaction.ConfirmAsync(
            "Delete NuGet version",
            $"Delete {PackageId} {row.Version} from {SelectedFeed.Label}? This cannot be undone; consumers on that version stop receiving it.");
        if (!confirmed)
        {
            return;
        }

        IsFeedBusy = true;
        try
        {
            var result = await _service.DeleteVersionAsync(
                SelectedFeed, FeedOwnerOrFolder, PackageId, row.Version, ApiKey, _writeLog, CancellationToken.None)
                ;
            FeedStatus = result.Message;
            _publisherService.AlertSounds.Play(result.Success ? AlertTone.Success : AlertTone.Failure);
            if (result.Success)
            {
                FeedVersions.Remove(row);
                OnPropertyChanged(nameof(HasFeedVersions));
            }
        }
        catch (Exception exception)
        {
            FeedStatus = $"Delete failed: {exception.Message}";
        }
        finally
        {
            IsFeedBusy = false;
        }
    }

    [RelayCommand]
    private void CopyPackCommand() => _ = _interaction.CopyTextToClipboardAsync(PackCommandPreview);

    private bool CanOpenPackageFolder() =>
        IsDesktopSession && HasPackedPackage
        && Path.GetDirectoryName(PackedPackagePath) is { Length: > 0 } folder
        && Directory.Exists(folder);

    [RelayCommand(CanExecute = nameof(CanOpenPackageFolder))]
    private void OpenPackageFolder()
    {
        var folder = Path.GetDirectoryName(PackedPackagePath);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            _publisherService.OpenPublishFolder(folder);
        }
    }

    [RelayCommand]
    private void ClearApiKey() => ApiKey = string.Empty;

    /// <summary>Resolves the packed artifact's identity, preferring the nuspec inside the .nupkg.</summary>
    private bool TryGetPackedIdentity(out NuGetPackageIdentity? identity, out string? folder)
    {
        identity = null;
        folder = null;
        if (!HasPackedPackage)
        {
            StatusMessage = "Pack the project first; there is no package to work with.";
            return false;
        }

        folder = Path.GetDirectoryName(PackedPackagePath);
        var report = _service.ValidatePackage(PackedPackagePath);
        identity = report.Identity ?? new NuGetPackageIdentity(PackageId, PackageVersion);
        return true;
    }
}
