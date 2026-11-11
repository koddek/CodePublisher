namespace CodePublisher.Features.Shared.Tools;

/// <summary>
/// Resolves an executable on this machine: the PATH first, then the named fallback locations.
/// The Windows search covers `.exe` and `.cmd` when the name carries no extension, matching what
/// the shell does for bare names. Pure host mechanics; no platform of the published app is
/// involved, so every library may use it.
/// </summary>
public static class HostExecutableResolver
{
    public static string? Resolve(string name, IReadOnlyList<string> candidates)
    {
        var executableNames = GetExecutableNames(name);
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathVariable))
        {
            foreach (var path in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var executableName in executableNames)
                {
                    var candidate = Path.Combine(path, executableName);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }

        foreach (var candidate in candidates)
        {
            foreach (var executableName in GetExecutableNames(Path.GetFileName(candidate)))
            {
                var resolvedCandidate = Path.Combine(
                    Path.GetDirectoryName(candidate) ?? string.Empty,
                    executableName);
                if (File.Exists(resolvedCandidate))
                {
                    return resolvedCandidate;
                }
            }
        }

        return null;
    }

    public static IReadOnlyList<string> GetExecutableNames(string name)
    {
        if (!OperatingSystem.IsWindows() || !string.IsNullOrEmpty(Path.GetExtension(name)))
        {
            return [name];
        }

        return [name + ".exe", name + ".cmd", name];
    }
}
