namespace CodePublisher.Features.Publishing.Configure;

/// <summary>
/// The desktop normalization both desktop hosts share: Native AOT always trims, and a user's
/// trimming choice is otherwise kept. Platform libraries apply their own tail flags after this.
/// </summary>
public static class DesktopPublishSettings
{
    public static PublishSettingsState Normalize(PublishSettingsState state)
        => state with
        {
            PublishTrimmed = state.PublishAot || state.PublishTrimmed
        };
}
