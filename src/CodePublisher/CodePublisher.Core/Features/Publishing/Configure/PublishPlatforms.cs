namespace CodePublisher.Features.Publishing.Configure;

public static class PublishPlatforms
{
    public const string Android = "Android";
    public const string MacOs = "macOS";
    public const string Windows = "Windows";
    public const string Ios = "iOS";
    public const string Linux = "Linux";
    public const string NuGet = "NuGet";

    /// <summary>
    /// Mac Catalyst is published through the Apple toolchain, not the desktop one: the target
    /// framework carries `-maccatalyst` and the runtime depends on an Apple SDK, not on the OS
    /// RID family. Both the target framework and the runtime identifier are checked so a project
    /// that declares either is treated as Catalyst.
    /// </summary>
    public static bool IsMacCatalyst(string targetFramework, string runtimeIdentifier)
    {
        return targetFramework.Contains("maccatalyst", StringComparison.OrdinalIgnoreCase)
            || runtimeIdentifier.Contains("maccatalyst", StringComparison.OrdinalIgnoreCase);
    }
}
