using System.Text;
using CodePublisher.Features.Publishing.Process;
using CodePublisher.Features.Shared.Tools;

namespace CodePublisher.Features.Deployment.Android;

/// <summary>
/// The tool locations on this machine that Android publishing and deployment depend on: adb, the
/// emulator, the signing tool, and the SDK layout. Everything Android-host specific that used to
/// live in the composition root lives here, so the composition root does not carry this platform's
/// probing rules and a change to adb resolution can never touch another platform.
/// </summary>
public static class AndroidTooling
{
    /// <summary>
    /// The adb and emulator this machine offers, newest adb first. Old copies are reported, never
    /// used, never removed: a package manager or an IDE owns those files.
    /// </summary>
    public static AndroidToolDiscovery Discover()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var androidHome = GetAndroidHome();
        var androidCandidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(androidHome))
        {
            androidCandidates.Add(Path.Combine(androidHome, "platform-tools/adb"));
            androidCandidates.Add(Path.Combine(androidHome, "emulator/emulator"));
        }

        // The most outdated copy on the machine wins a "first found" search, which is how a host with
        // the SDK's 37.x and a package manager's 36.x ends up pairing with the older one: the phone's
        // pairing service speaks a newer protocol and the pair fails with a protocol fault. The
        // candidates are therefore versioned and the newest one is used.
        var adbCandidatePaths = new List<string>
        {
            Path.Combine(home, "Library/Android/sdk/platform-tools/adb"),
            Path.Combine(home, "Android/Sdk/platform-tools/adb"),
            Path.Combine(home, "Library/Developer/Xamarin/android-sdk-macosx/platform-tools/adb"),
            Path.Combine(home, "AppData/Local/Android/Sdk/platform-tools/adb.exe"),
            "/opt/homebrew/bin/adb",
            "/usr/local/bin/adb",
            "/usr/bin/adb",
            "/opt/homebrew/share/android-commandlinetools/platform-tools/adb"
        };
        const string caskroom = "/opt/homebrew/Caskroom/android-platform-tools";
        if (Directory.Exists(caskroom))
        {
            try
            {
                adbCandidatePaths.AddRange(
                    Directory.EnumerateFiles(caskroom, "adb", SearchOption.AllDirectories));
            }
            catch
            {
                // Best effort.
            }
        }

        adbCandidatePaths.AddRange(
            androidCandidates.Where(p => p.EndsWith("adb", StringComparison.OrdinalIgnoreCase)));
        var adbCandidates = ResolveAdbCandidates(adbCandidatePaths.ToArray());

        var emulatorPath = HostExecutableResolver.Resolve("emulator",
        [
            Path.Combine(home, "Library/Android/sdk/emulator/emulator"),
            Path.Combine(home, "Android/Sdk/emulator/emulator"),
            "/opt/homebrew/bin/emulator",
            "/usr/local/bin/emulator",
            .. androidCandidates.Where(p => p.EndsWith("emulator", StringComparison.OrdinalIgnoreCase))
        ]);

        return new AndroidToolDiscovery(
            adbCandidates.NewestPath,
            adbCandidates.Detected,
            adbCandidates.Installations,
            emulatorPath);
    }

    public static string? ResolveApkSignerPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var androidHome = GetAndroidHome();
        var candidates = new List<string>
        {
            Path.Combine(home, "Library/Android/sdk/build-tools"),
            Path.Combine(home, "Android/Sdk/build-tools"),
            "/opt/homebrew/share/android-commandlinetools/build-tools",
            "/usr/local/share/android-commandlinetools/build-tools"
        };
        if (!string.IsNullOrWhiteSpace(androidHome))
        {
            candidates.Insert(0, Path.Combine(androidHome, "build-tools"));
        }

        foreach (var buildToolsRoot in candidates)
        {
            if (!Directory.Exists(buildToolsRoot))
            {
                continue;
            }

            var apksignerPath = Directory
                .EnumerateDirectories(buildToolsRoot)
                .Select(directory => Path.Combine(directory, "apksigner"))
                .Where(File.Exists)
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(apksignerPath))
            {
                return apksignerPath;
            }
        }

        return HostExecutableResolver.Resolve("apksigner",
        [
            "/opt/homebrew/bin/apksigner",
            "/usr/local/bin/apksigner"
        ]);
    }

    public static string GetAndroidHome()
    {
        var androidHome = Environment.GetEnvironmentVariable("ANDROID_HOME");
        return !string.IsNullOrWhiteSpace(androidHome)
            ? androidHome
            : Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT") ?? string.Empty;
    }

    private static AdbCandidates ResolveAdbCandidates(params string[] known)
    {
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in known)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            foreach (var executableName in HostExecutableResolver.GetExecutableNames(Path.GetFileName(candidate)))
            {
                var resolved = Path.Combine(Path.GetDirectoryName(candidate) ?? string.Empty, executableName);
                if (File.Exists(resolved) && seen.Add(resolved))
                {
                    paths.Add(resolved);
                }
            }
        }

        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathVariable))
        {
            foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var executableName in HostExecutableResolver.GetExecutableNames("adb"))
                {
                    var resolved = Path.Combine(directory, executableName);
                    if (File.Exists(resolved) && seen.Add(resolved))
                    {
                        paths.Add(resolved);
                    }
                }
            }
        }

        var found = new List<(string, AndroidAdbVersion)>();
        foreach (var path in paths)
        {
            found.Add((path, ReadAdbVersion(path)));
        }

        return new AdbCandidates(found);
    }

    private static AndroidAdbVersion ReadAdbVersion(string adbPath)
    {
        // This runs on the application's start-up path, which is the UI thread inside
        // OnFrameworkInitializationCompleted. ProcessRunner.RunAsync awaits
        // Process.WaitForExitAsync, and that continuation is posted to the SynchronizationContext
        // the caller captured (the Avalonia dispatcher), so blocking on the task from the UI
        // thread deadlocks: the task needs the very thread that is waiting for it, the
        // constructor never returns, no window is created, and the Dock icon bounces forever.
        // A thread-pool thread has no SynchronizationContext to capture, so the wait completes.
        try
        {
            return Task.Run(() => ReadAdbVersionAsync(adbPath)).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // An adb that cannot report a version stays at 0, so it never wins over one that can.
        }

        return AndroidAdbVersion.Parse(null);
    }

    private static async Task<AndroidAdbVersion> ReadAdbVersionAsync(string adbPath)
    {
        var output = new StringBuilder();
        await ProcessRunner.RunAsync([adbPath, "version"], text => output.Append(text), CancellationToken.None)
            .ConfigureAwait(false);
        return AndroidAdbVersion.Parse(output.ToString());
    }

    private sealed record AdbCandidates(IReadOnlyList<(string Path, AndroidAdbVersion Version)> Found)
    {
        /// <summary>The newest adb on this machine, and its version; null when none was found.</summary>
        public (string? Path, AndroidAdbVersion? Version) Newest =>
            Found
                .Select((candidate, index) => (candidate, index))
                .OrderByDescending(entry => entry.candidate.Version)
                .ThenBy(entry => entry.index)
                .Select(entry => ((string?)entry.candidate.Path, (AndroidAdbVersion?)entry.candidate.Version))
                .FirstOrDefault();

        public string? NewestPath => Newest.Path;

        public AndroidAdbVersion? Detected => Newest.Version;

        /// <summary>Newest first, with the app's own choice flagged on the first entry.</summary>
        public IReadOnlyList<AndroidAdbInstallation> Installations =>
            Found
                .OrderByDescending(candidate => candidate.Version)
                .Select((candidate, index) => new AndroidAdbInstallation(candidate.Path, candidate.Version, index == 0))
                .ToArray();
    }
}

/// <summary>The tool discovery results: the adb to run, its version, and every copy this machine has.</summary>
public sealed record AndroidToolDiscovery(
    string? AdbPath,
    AndroidAdbVersion? AdbVersion,
    IReadOnlyList<AndroidAdbInstallation> AdbInstallations,
    string? EmulatorPath);
