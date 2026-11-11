namespace CodePublisher.Features.Publishing.Configure;

/// <summary>
/// Normalizes a publish settings snapshot so the options agree with each other and with the
/// platform's rules. The capability gates the platforms share run first; each platform library
/// then applies everything its own platform decides, and the dependency graph runs on top.
/// </summary>
public static class PublishSettingsPolicy
{
    /// <summary>
    /// Normalizes the options: the platform rules and the dependency graph are applied together
    /// until the result settles.
    /// </summary>
    public static PublishSettingsState Normalize(PublishSettingsState state) =>
        PublishOptionResolver.Resolve(state).State;

    /// <summary>
    /// Normalizes the options after the user changed one of them. The changed option is what
    /// settles a mutual exclusion and what tells the resolver whether a prerequisite was turned
    /// off on purpose.
    /// </summary>
    public static PublishSettingsState Normalize(
        PublishSettingsState state,
        PublishOptionId changedOption,
        bool changedValue = true) =>
        PublishOptionResolver.Resolve(state, changedOption, changedValue).State;

    /// <summary>
    /// The platform rules that do not come from the dependency graph: what each platform supports
    /// at all, the values the Android tooling pair is allowed to take, and the options a platform
    /// decides on the user's behalf.
    ///
    /// These rules never settle a cross-option relationship on their own. That is the dependency
    /// graph's job, so the two can never disagree: if the platform rules turned an option off
    /// because a companion was missing, the graph could never turn that companion on.
    /// </summary>
    public static PublishSettingsState ApplyPlatformRules(PublishSettingsState state)
    {
        // Capability gates: an option the platform cannot do at all is turned off.
        var publishAot = state.PublishAot &&
            PublishOptionPolicy.IsPublishAotEnabled(
                state.PublishPlatform,
                state.TargetFramework,
                state.RuntimeIdentifier);
        var readyToRun = state.PublishReadyToRun &&
            PublishOptionPolicy.IsReadyToRunEnabled(
                publishAot,
                state.PublishPlatform,
                state.TargetFramework,
                state.RuntimeIdentifier);
        var singleFile = state.PublishSingleFile &&
            PublishOptionPolicy.IsPublishSingleFileEnabled(publishAot, state.PublishPlatform);

        // iOS never produces an application host; every other platform passes its own value.
        var useAppHost = !string.Equals(state.PublishPlatform, PublishPlatforms.Ios, StringComparison.Ordinal)
            && state.UseAppHost;

        state = state with
        {
            PublishAot = publishAot,
            PublishReadyToRun = readyToRun,
            PublishSingleFile = singleFile,
            UseAppHost = useAppHost
        };

        var isMacOs = string.Equals(state.PublishPlatform, PublishPlatforms.MacOs, StringComparison.Ordinal);
        var isCatalyst = isMacOs
            && PublishPlatforms.IsMacCatalyst(state.TargetFramework, state.RuntimeIdentifier);

        return state.PublishPlatform switch
        {
            PublishPlatforms.Android => AndroidPublishSettings.Apply(state),
            PublishPlatforms.Ios => ApplePublishSettings.Apply(state),
            PublishPlatforms.MacOs => isCatalyst
                ? ApplePublishSettings.ApplyCatalyst(state)
                : MacPublishSettings.Apply(state),
            PublishPlatforms.Windows => WindowsPublishSettings.Apply(state),
            PublishPlatforms.Linux => LinuxPublishSettings.Apply(state),
            PublishPlatforms.NuGet => NuGetPublishSettings.Apply(state),
            _ => state
        };
    }
}
