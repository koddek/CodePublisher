using System.Diagnostics;

namespace DotNetAppPublisher.Services;

/// <summary>Which alert a finished task plays.</summary>
public enum AlertTone
{
    /// <summary>The task completed.</summary>
    Success,

    /// <summary>The task failed, was refused, or needs attention.</summary>
    Failure
}

/// <summary>Operating systems with their own alert playback.</summary>
public enum AlertSoundPlatform
{
    MacOs,
    Windows,
    Linux,
    Other
}

/// <summary>One playback attempt: an executable plus its arguments.</summary>
public sealed record AlertSoundCommand(string FileName, IReadOnlyList<string> Arguments);

/// <summary>
/// Plays the short alert that marks a finished task. Publishing, deploy actions, device file transfers,
/// and clipboard copies all use this one service, so the app sounds the same everywhere and can be muted
/// in one place. Playback never blocks the caller and never throws: an unavailable player falls through
/// to the next candidate and finally to the terminal bell.
/// </summary>
public sealed class AlertSoundService
{
    private static readonly string[] MacOsSuccessSounds =
    [
        "/System/Library/Sounds/Glass.aiff",
        "/System/Library/Sounds/Ping.aiff",
        "/System/Library/Sounds/Blow.aiff"
    ];

    private static readonly string[] MacOsFailureSounds =
    [
        "/System/Library/Sounds/Basso.aiff",
        "/System/Library/Sounds/Sosumi.aiff",
        "/System/Library/Sounds/Funk.aiff"
    ];

    private static readonly string[] WindowsSuccessSounds =
    [
        @"C:\Windows\Media\Windows Notify System Generic.wav",
        @"C:\Windows\Media\notify.wav",
        @"C:\Windows\Media\chimes.wav"
    ];

    private static readonly string[] WindowsFailureSounds =
    [
        @"C:\Windows\Media\Windows Critical Stop.wav",
        @"C:\Windows\Media\Windows Ding.wav",
        @"C:\Windows\Media\chord.wav"
    ];

    private static readonly string[] LinuxSuccessSounds =
    [
        "/usr/share/sounds/freedesktop/stereo/complete.oga",
        "/usr/share/sounds/freedesktop/stereo/bell.oga",
        "/usr/share/sounds/gnome/default/alerts/glass.ogg"
    ];

    private static readonly string[] LinuxFailureSounds =
    [
        "/usr/share/sounds/freedesktop/stereo/dialog-error.oga",
        "/usr/share/sounds/freedesktop/stereo/dialog-warning.oga",
        "/usr/share/sounds/gnome/default/alerts/bark.ogg"
    ];

    /// <summary>
    /// Alerts closer together than this are dropped. A single user action can report twice (Google Play
    /// publishing rings when the build finishes and again when the upload stage completes), which must
    /// sound like one alert, not two.
    /// </summary>
    public const int CoalesceWindowMilliseconds = 700;

    private long _lastPlayedAtMilliseconds = long.MinValue;

    /// <summary>When false no alert is played; the UI exposes this as the speaker toggle.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Whether an alert asked for at <paramref name="nowMilliseconds"/> is too soon to play.</summary>
    public static bool IsCoalesced(long nowMilliseconds, long previousMilliseconds) =>
        previousMilliseconds != long.MinValue
        && nowMilliseconds - previousMilliseconds < CoalesceWindowMilliseconds;

    /// <summary>
    /// Plays the alert for a finished task. Detached from the caller on purpose: a slow player (the
    /// Windows PowerShell one in particular) must never delay the UI or the operation that just ended.
    /// </summary>
    public void Play(AlertTone tone)
    {
        if (!IsEnabled || OperatingSystem.IsBrowser())
        {
            return;
        }

        var now = Environment.TickCount64;
        if (IsCoalesced(now, Interlocked.Read(ref _lastPlayedAtMilliseconds)))
        {
            return;
        }

        Interlocked.Exchange(ref _lastPlayedAtMilliseconds, now);
        _ = Task.Run(() => PlayCore(tone));
    }

    /// <summary>Playback candidates for the current platform, most preferred first. Pure so tests pin them.</summary>
    public static IReadOnlyList<AlertSoundCommand> BuildPlan(
        AlertTone tone,
        AlertSoundPlatform platform,
        Func<string, bool> fileExists)
    {
        var commands = new List<AlertSoundCommand>();
        switch (platform)
        {
            case AlertSoundPlatform.MacOs:
                AddFilePlayer(commands, "afplay", FirstExisting(MacOsSounds(tone), fileExists));
                break;
            case AlertSoundPlatform.Windows:
                AddWindowsPlayer(commands, FirstExisting(WindowsSounds(tone), fileExists));
                break;
            case AlertSoundPlatform.Linux:
                AddFilePlayer(commands, "paplay", FirstExisting(LinuxSounds(tone), fileExists));
                AddFilePlayer(commands, "pw-play", FirstExisting(LinuxSounds(tone), fileExists));
                commands.Add(new AlertSoundCommand(
                    "canberra-gtk-play",
                    ["-i", tone == AlertTone.Success ? "complete" : "dialog-error"]));
                break;
            default:
                break;
        }

        return commands;
    }

    /// <summary>The platform whose playback plan <see cref="Play"/> uses.</summary>
    public static AlertSoundPlatform CurrentPlatform() =>
        OperatingSystem.IsMacOS() ? AlertSoundPlatform.MacOs
        : OperatingSystem.IsWindows() ? AlertSoundPlatform.Windows
        : OperatingSystem.IsLinux() ? AlertSoundPlatform.Linux
        : AlertSoundPlatform.Other;

    private void PlayCore(AlertTone tone)
    {
        try
        {
            var plan = BuildPlan(tone, CurrentPlatform(), File.Exists);
            foreach (var command in plan)
            {
                var startInfo = new ProcessStartInfo(command.FileName)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                foreach (var argument in command.Arguments)
                {
                    startInfo.ArgumentList.Add(argument);
                }

                try
                {
                    using var process = Process.Start(startInfo);
                    if (process is not null)
                    {
                        return;
                    }
                }
                catch
                {
                    // Player not installed: fall through to the next candidate.
                }
            }

            // Last resort: the terminal bell. Beeps only where the platform supports one.
            Console.Write("\a");
        }
        catch
        {
            // An alert must never fail the task it reports on.
        }
    }

    private static void AddFilePlayer(List<AlertSoundCommand> commands, string executable, string? soundFile)
    {
        if (soundFile is not null)
        {
            commands.Add(new AlertSoundCommand(executable, [soundFile]));
        }
    }

    private static void AddWindowsPlayer(List<AlertSoundCommand> commands, string? soundFile)
    {
        if (soundFile is null)
        {
            return;
        }

        // PlaySync waits for the sound to finish, but this runs on a background thread, so the UI and the
        // finished operation are unaffected.
        var script = $"$player = New-Object Media.SoundPlayer '{soundFile}'; $player.PlaySync()";
        commands.Add(new AlertSoundCommand(
            "powershell",
            ["-NoProfile", "-NonInteractive", "-Command", script]));
    }

    private static IReadOnlyList<string> MacOsSounds(AlertTone tone) =>
        tone == AlertTone.Success ? MacOsSuccessSounds : MacOsFailureSounds;

    private static IReadOnlyList<string> WindowsSounds(AlertTone tone) =>
        tone == AlertTone.Success ? WindowsSuccessSounds : WindowsFailureSounds;

    private static IReadOnlyList<string> LinuxSounds(AlertTone tone) =>
        tone == AlertTone.Success ? LinuxSuccessSounds : LinuxFailureSounds;

    private static string? FirstExisting(IReadOnlyList<string> candidates, Func<string, bool> fileExists)
    {
        foreach (var candidate in candidates)
        {
            if (fileExists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
