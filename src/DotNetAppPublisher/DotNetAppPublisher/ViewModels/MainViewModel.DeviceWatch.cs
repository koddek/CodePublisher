using CommunityToolkit.Mvvm.ComponentModel;
using DotNetAppPublisher.Features.Deployment.Android;

namespace DotNetAppPublisher.ViewModels;

/// <summary>
/// Keeps the Android target list live instead of making the user press Refresh.
///
/// Two loops share the decisions in <see cref="AndroidDeviceWatch"/>: a cheap `adb devices -l` poll while
/// a section that shows targets is open, which notices a phone being plugged in, authorized, unplugged,
/// or an emulator appearing; and a focused wait after an emulator is started or a device is connected
/// over Wi-Fi, which polls once a second, keeps the list live while the target boots, and selects what
/// appeared.
/// </summary>
public partial class MainViewModel
{
    private static readonly TimeSpan DeviceWatchInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan LaunchedTargetPollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LaunchedTargetTimeout = TimeSpan.FromMinutes(3);
    private const string AutoRefreshStatus = "Watching for devices — the list refreshes itself.";

    private readonly SemaphoreSlim _deviceRefreshGate = new(1, 1);
    private CancellationTokenSource? _deviceWatchCancellation;
    private Task? _deviceWatchTask;
    private CancellationTokenSource? _launchedTargetCancellation;
    private bool _isWaitingForLaunchedTarget;

    /// <summary>What the device card says about watching: idle hint, or the target currently being awaited.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDeviceWatchStatusVisible))]
    private string _deviceWatchStatus = string.Empty;

    public bool IsDeviceWatchStatusVisible => IsAndroidPlatform && DeviceWatchStatus.Length > 0;

    public bool IsDeviceWatchAvailable => !OperatingSystem.IsBrowser() && _publisherService.AdbPath is not null;

    /// <summary>Starts watching when this build, the adb tool, and the platform allow it. Idempotent.</summary>
    public void StartDeviceWatch()
    {
        if (!IsDeviceWatchAvailable || !IsAndroidPlatform)
        {
            StopDeviceWatch();
            return;
        }

        if (_deviceWatchTask is { IsCompleted: false })
        {
            return;
        }

        _deviceWatchCancellation = new CancellationTokenSource();
        DeviceWatchStatus = AutoRefreshStatus;
        _deviceWatchTask = WatchAndroidDevicesAsync(_deviceWatchCancellation.Token);
    }

    /// <summary>Stops watching and clears the hint. Called when leaving every section that shows targets.</summary>
    public void StopDeviceWatch()
    {
        var cancellation = _deviceWatchCancellation;
        _deviceWatchCancellation = null;
        _deviceWatchTask = null;
        cancellation?.Cancel();
        cancellation?.Dispose();

        var targetWait = _launchedTargetCancellation;
        _launchedTargetCancellation = null;
        targetWait?.Cancel();
        targetWait?.Dispose();
        _isWaitingForLaunchedTarget = false;
        DeviceWatchStatus = string.Empty;
    }

    /// <summary>
    /// Replaces the visible target list with a fresh discovery. The user's selection is kept while that
    /// target is still listed; when it is gone, or when a target just became usable (an emulator that
    /// finished booting, a phone whose debugging prompt was approved), that target is selected so
    /// auto-refresh never leaves the UI pointing at nothing.
    /// </summary>
    private AndroidDeviceChange ApplyDiscoveredDevices(IReadOnlyList<AndroidDeviceInfo> devices)
    {
        var previousSelection = SelectedDevice;
        var change = AndroidDeviceWatch.Compare(AvailableAndroidDevices.ToArray(), devices);

        AvailableAndroidDevices.Clear();
        foreach (var device in devices)
        {
            AvailableAndroidDevices.Add(device);
        }

        var keptSelection = previousSelection is null
            ? null
            : AvailableAndroidDevices.FirstOrDefault(candidate => IsSameAndroidTarget(candidate, previousSelection));
        var appeared = change.Added
            .Concat(change.BecameReady)
            .Where(device => device.IsReady)
            .OrderBy(AndroidDeviceInfo.Rank)
            .FirstOrDefault();

        SelectedDevice = keptSelection
            ?? AvailableAndroidDevices.FirstOrDefault(candidate => appeared is not null && IsSameAndroidTarget(candidate, appeared))
            ?? AvailableAndroidDevices.FirstOrDefault(device => device.IsRunning)
            ?? AvailableAndroidDevices.FirstOrDefault();
        return change;
    }

    /// <summary>
    /// The serials on screen right now. Taken before a launch so the wait that follows can tell which
    /// target the action produced, even if a refresh lands while the emulator is starting.
    /// </summary>
    private string[] CaptureKnownSerials() =>
        AvailableAndroidDevices
            .Where(device => !string.IsNullOrWhiteSpace(device.Serial))
            .Select(device => device.Serial)
            .ToArray();

    /// <summary>
    /// A refresh quiet enough for the watcher: no busy bar, no counts, and a status line only when
    /// something actually changed. Skips itself when another refresh is already running.
    /// </summary>
    private async Task RefreshDevicesSilentlyAsync(CancellationToken cancellationToken)
    {
        if (!await _deviceRefreshGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            var devices = await _publisherService.DiscoverAndroidDevicesAsync(cancellationToken);
            var change = ApplyDiscoveredDevices(devices);
            var changeText = AndroidDeviceWatch.DescribeChange(change);
            if (changeText is not null)
            {
                StatusMessage = changeText;
                AppendLog($"{Environment.NewLine}[Devices] {changeText}{Environment.NewLine}");
            }
        }
        finally
        {
            _deviceRefreshGate.Release();
        }
    }

    private async Task WatchAndroidDevicesAsync(CancellationToken cancellationToken)
    {
        var consecutiveFailures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(DeviceWatchInterval, cancellationToken);

                // The launched-target wait owns adb while it runs, and a task the user started (publish,
                // install) must not be raced by a phone being plugged in.
                if (_isWaitingForLaunchedTarget || IsBusy || !IsAndroidPlatform || _publisherService.AdbPath is null)
                {
                    continue;
                }

                var probe = await _publisherService.ProbeAndroidDevicesAsync(cancellationToken);
                consecutiveFailures = 0;
                if (!AndroidDeviceWatch.NeedsRefresh(AndroidDeviceWatch.Fingerprint(AvailableAndroidDevices), probe))
                {
                    continue;
                }

                await RefreshDevicesSilentlyAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // adb can hiccup once (a restart, a device dropping mid-query); three in a row means the
                // watcher should stop rather than keep spawning a failing process.
                consecutiveFailures++;
                if (consecutiveFailures < 3)
                {
                    continue;
                }

                AppendLog($"{Environment.NewLine}[Devices] Auto-refresh stopped: {ex.Message}{Environment.NewLine}");
                StatusMessage = $"Auto-refresh stopped: {ex.Message}";
                StopDeviceWatch();
                return;
            }
        }
    }

    /// <summary>
    /// Waits for what a launch or a wireless connect just produced. Called from those paths so the target
    /// appears, is selected, and rings the alert without a manual refresh.
    /// </summary>
    private void BeginWaitingForLaunchedTarget(
        string what,
        string? expectedAvdName = null,
        TimeSpan? timeout = null,
        IReadOnlyCollection<string>? knownSerials = null)
    {
        if (!IsDeviceWatchAvailable || !IsAndroidPlatform)
        {
            return;
        }

        knownSerials ??= CaptureKnownSerials();

        var previousWait = _launchedTargetCancellation;
        previousWait?.Cancel();
        previousWait?.Dispose();

        var cancellation = new CancellationTokenSource();
        _launchedTargetCancellation = cancellation;
        _isWaitingForLaunchedTarget = true;
        DeviceWatchStatus = $"Waiting for {what} to come online…";
        _ = WaitForLaunchedTargetAsync(
            knownSerials,
            expectedAvdName,
            what,
            timeout ?? LaunchedTargetTimeout,
            cancellation);
    }

    private async Task WaitForLaunchedTargetAsync(
        IReadOnlyCollection<string> knownSerials,
        string? expectedAvdName,
        string what,
        TimeSpan timeout,
        CancellationTokenSource cancellation)
    {
        var cancellationToken = cancellation.Token;
        var deadline = DateTimeOffset.UtcNow + timeout;
        try
        {
            while (DateTimeOffset.UtcNow < deadline)
            {
                var probe = await _publisherService.ProbeAndroidDevicesAsync(cancellationToken);
                var cameOnline = probe.FirstOrDefault(entry =>
                    string.Equals(entry.State, AndroidDeviceState.Ready, StringComparison.Ordinal)
                    && !knownSerials.Contains(entry.Serial, StringComparer.OrdinalIgnoreCase));
                if (cameOnline is not null)
                {
                    var devices = await _publisherService.DiscoverAndroidDevicesAsync(cancellationToken);
                    var change = ApplyDiscoveredDevices(devices);
                    var target = AndroidDeviceWatch.ChooseLaunchedTarget(devices, knownSerials, expectedAvdName, cameOnline.Serial);
                    if (target is not null)
                    {
                        StatusMessage = $"{target.DisplayName} is online and selected.";
                        AppendLog($"{Environment.NewLine}[{what}] {StatusMessage}{Environment.NewLine}");
                        PlayCompletionAlert(failed: false);
                        return;
                    }

                    var changeText = AndroidDeviceWatch.DescribeChange(change);
                    if (changeText is not null)
                    {
                        AppendLog($"{Environment.NewLine}[Devices] {changeText}{Environment.NewLine}");
                    }
                }

                await Task.Delay(LaunchedTargetPollInterval, cancellationToken);
            }

            StatusMessage = $"{what} has not come online yet. The device list keeps refreshing, so it will appear here as soon as adb sees it.";
            AppendLog($"{Environment.NewLine}[{what}] {StatusMessage}{Environment.NewLine}");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusMessage = $"{what}: waiting for the device failed.";
            AppendLog($"{Environment.NewLine}[{what}] {ex.Message}{Environment.NewLine}");
        }
        finally
        {
            // Only clear the state when this wait is still the current one: starting another launch
            // replaces the waiter, and the old one must not blank the new one's hint.
            if (ReferenceEquals(_launchedTargetCancellation, cancellation))
            {
                _isWaitingForLaunchedTarget = false;
                DeviceWatchStatus = IsAndroidPlatform && IsDeviceWatchAvailable ? AutoRefreshStatus : string.Empty;
            }
        }
    }
}
