namespace DotNetAppPublisher.Features.Publishing.Configure;

public sealed record PublishSettingsState(
    string PublishPlatform,
    string TargetFramework,
    string RuntimeIdentifier,
    bool SelfContained,
    bool PublishAot,
    bool PublishReadyToRun,
    bool PublishSingleFile,
    bool PublishTrimmed,
    string AndroidLinkMode,
    string AndroidLinkTool,
    string AndroidDexTool,
    bool RunAotCompilation,
    bool EnableProfiledAot,
    bool IncludeApk,
    bool IncludeAab);

public static class PublishSettingsPolicy
{
    public static PublishSettingsState Normalize(PublishSettingsState state)
    {
        var publishAot = state.PublishAot
            && PublishOptionPolicy.IsPublishAotEnabled(
                state.PublishPlatform,
                state.TargetFramework,
                state.RuntimeIdentifier);
        var selfContained = state.SelfContained || publishAot;
        var readyToRun = state.PublishReadyToRun &&
            PublishOptionPolicy.IsReadyToRunEnabled(
                publishAot,
                state.PublishPlatform,
                state.TargetFramework,
                state.RuntimeIdentifier);
        var singleFile = state.PublishSingleFile &&
            PublishOptionPolicy.IsPublishSingleFileEnabled(publishAot, state.PublishPlatform);

        var runAot = state.RunAotCompilation &&
            (!string.Equals(state.PublishPlatform, PublishPlatforms.Android, StringComparison.Ordinal) ||
             PublishOptionPolicy.IsAndroidAotEnabled(state.AndroidLinkMode));
        var profiledAot = state.EnableProfiledAot && runAot;

        var trimmed = state.PublishTrimmed;
        if (string.Equals(state.PublishPlatform, PublishPlatforms.Android, StringComparison.Ordinal))
        {
            var androidLinkEnabled = PublishOptionPolicy.IsAndroidAotEnabled(state.AndroidLinkMode);
            trimmed = selfContained && androidLinkEnabled;
        }
        else if (string.Equals(state.PublishPlatform, PublishPlatforms.Ios, StringComparison.Ordinal)
            || string.Equals(state.PublishPlatform, PublishPlatforms.MacOs, StringComparison.Ordinal)
                && PlatformProfiles.IsMacCatalyst(state.TargetFramework, state.RuntimeIdentifier))
        {
            trimmed = true;
        }
        else
        {
            trimmed = selfContained && !publishAot && trimmed;
        }

        var includeApk = state.IncludeApk;
        var includeAab = state.IncludeAab;
        if (!includeApk && !includeAab)
        {
            includeAab = true;
        }

        var androidLinkTool = state.AndroidLinkTool.Trim();
        var androidDexTool = state.AndroidDexTool.Trim();
        if (string.Equals(state.PublishPlatform, PublishPlatforms.Android, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(PublishOptionPolicy.AndroidToolValidationMessage(androidLinkTool, androidDexTool)))
        {
            androidLinkTool = "r8";
            androidDexTool = "d8";
        }

        return state with
        {
            SelfContained = selfContained,
            PublishAot = publishAot,
            PublishReadyToRun = readyToRun,
            PublishSingleFile = singleFile,
            PublishTrimmed = trimmed,
            AndroidLinkTool = androidLinkTool,
            AndroidDexTool = androidDexTool,
            RunAotCompilation = runAot,
            EnableProfiledAot = profiledAot,
            IncludeApk = includeApk,
            IncludeAab = includeAab
        };
    }
}
