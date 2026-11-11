namespace CodePublisher.Features.Publishing.Configure;

/// <summary>
/// The platform the app publishes, keyed by name. Each platform's defaults live in that
/// platform's library; this registry only maps a publish platform to its profile.
/// </summary>
public static class PlatformProfiles
{
    public static PlatformProfile GetDefaults(string publishPlatform) =>
        publishPlatform switch
        {
            PublishPlatforms.Android => AndroidPublishDefaults.Default,
            PublishPlatforms.MacOs => MacPublishDefaults.Default,
            PublishPlatforms.Windows => WindowsPublishDefaults.Default,
            PublishPlatforms.Ios => IosPublishDefaults.Default,
            PublishPlatforms.Linux => LinuxPublishDefaults.Default,
            PublishPlatforms.NuGet => NuGetPublishDefaults.Default,
            _ => MacPublishDefaults.Default
        };
}
