namespace DotNetAppPublisher.Features.Deployment.Android;

/// <summary>
/// Decides when a paired Wi-Fi phone is worth another connection attempt while the app is watching.
///
/// A paired phone stops answering when its screen goes off, Wireless debugging is closed, or it leaves
/// the network, and it starts publishing its debugging port again when it wakes. The app turns that into
/// a background retry: one host at a time, a backoff after each failure, and nothing at all for a host
/// that adb already lists as usable. Pure: the caller supplies the state and the clock.
/// </summary>
public static class AndroidWirelessReconnect
{
    /// <summary>How long the watcher waits before the first retry of a host that is not connected.</summary>
    public static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The longest wait between attempts, so a phone that wakes is noticed within about a minute
    /// without adb being asked to discover and connect in a tight loop.
    /// </summary>
    public static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The wait before the next attempt after <paramref name="consecutiveFailures"/> failed ones: prompt
    /// at first, backing off while the phone stays away. A host that has never failed still waits
    /// <see cref="FirstRetryDelay"/> after its last attempt, because the attempt itself takes seconds and
    /// the phone needs a moment after it wakes.
    /// </summary>
    public static TimeSpan RetryDelay(int consecutiveFailures) => consecutiveFailures switch
    {
        <= 0 => FirstRetryDelay,
        1 => TimeSpan.FromSeconds(20),
        2 => TimeSpan.FromSeconds(30),
        _ => MaximumRetryDelay
    };

    /// <summary>
    /// The paired host worth a reconnect attempt now, or null when nothing is due.
    ///
    /// A host adb already lists as usable is never retried. Among the rest, a host that has never been
    /// attempted comes first, then the one attempted longest ago, so a single phone that comes back is
    /// connected first and several paired phones take turns.
    /// </summary>
    public static string? ChooseHost(
        IReadOnlyDictionary<string, string?> pairedHosts,
        IReadOnlyCollection<string> readyHosts,
        IReadOnlyDictionary<string, DateTimeOffset> lastAttemptByHost,
        IReadOnlyDictionary<string, int> failuresByHost,
        DateTimeOffset now)
    {
        string? bestHost = null;
        var bestLastAttempt = DateTimeOffset.MaxValue;

        foreach (var (host, _) in pairedHosts)
        {
            if (string.IsNullOrWhiteSpace(host) || readyHosts.Contains(host))
            {
                continue;
            }

            var failures = failuresByHost.TryGetValue(host, out var count) ? count : 0;
            var lastAttempt = lastAttemptByHost.TryGetValue(host, out var attemptedAt)
                ? attemptedAt
                : DateTimeOffset.MinValue;
            if (now - lastAttempt < RetryDelay(failures))
            {
                continue;
            }

            if (lastAttempt < bestLastAttempt)
            {
                bestHost = host;
                bestLastAttempt = lastAttempt;
            }
        }

        return bestHost;
    }
}
