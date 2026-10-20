namespace DotNetAppPublisher.Features.Publishing.Configure;

/// <summary>
/// A mutable view over a publish settings snapshot, addressed by option id.
///
/// The resolver needs to read and write individual options while it walks the dependency graph,
/// and it needs to remember the user's intent for each option: an option the user turned off
/// stays off, and an option turned off as a consequence is never turned back on.
/// </summary>
public sealed class PublishOptionValues
{
    private readonly HashSet<PublishOptionId> _userDisabled = [];
    private readonly HashSet<PublishOptionId> _disabledDuringResolution = [];

    private PublishOptionValues(PublishSettingsState state)
    {
        State = state;
    }

    /// <summary>The snapshot being resolved.</summary>
    public PublishSettingsState State { get; internal set; }

    /// <summary>Starts a resolution from a settings snapshot.</summary>
    public static PublishOptionValues From(PublishSettingsState state) => new(state);

    /// <summary>
    /// The option the user changed last. When two mutually exclusive options are both on, this
    /// decides which one survives: the user's most recent choice wins.
    /// </summary>
    public PublishOptionId? LastChanged { get; private set; }

    /// <summary>Records the option the user changed, and whether they turned it on or off.</summary>
    public void MarkUserChange(PublishOptionId option, bool newValue)
    {
        LastChanged = option;
        if (!newValue)
        {
            _userDisabled.Add(option);
        }
        else
        {
            _userDisabled.Remove(option);
        }
    }

    /// <summary>
    /// Applies the user's change to the snapshot, so the resolution starts from what the user
    /// actually asked for rather than from the previous state.
    /// </summary>
    public void ApplyUserChange(PublishOptionId option, bool newValue)
    {
        if (option == PublishOptionId.AndroidLinkMode)
        {
            // The link mode is a choice: "None" is the value that turns the linker off.
            State = SetValue(option, newValue ? "SdkOnly" : "None");
            return;
        }

        if (newValue)
        {
            Set(option, true);
            return;
        }

        State = SetToggle(option, false);
    }

    /// <summary>True when the given option is the one the user changed last.</summary>
    public bool WasChanged(PublishOptionId option) => LastChanged == option;

    /// <summary>True when the user turned the option off on purpose.</summary>
    public bool WasDisabledByUser(PublishOptionId option) => _userDisabled.Contains(option);

    /// <summary>True when the option was turned off during this resolution.</summary>
    public bool WasDisabledDuringResolution(PublishOptionId option) => _disabledDuringResolution.Contains(option);

    /// <summary>True when the option holds a value rather than being on or off.</summary>
    public static bool IsChoice(PublishOptionId option) => option is
        PublishOptionId.AndroidLinkMode or
        PublishOptionId.AndroidLinkTool or
        PublishOptionId.AndroidDexTool;

    /// <summary>True when the option is on. A choice option counts as on when it has a value.</summary>
    public bool IsOn(PublishOptionId option) => option switch
    {
        PublishOptionId.SelfContained => State.SelfContained,
        PublishOptionId.PublishTrimmed => State.PublishTrimmed,
        PublishOptionId.PublishAot => State.PublishAot,
        PublishOptionId.PublishReadyToRun => State.PublishReadyToRun,
        PublishOptionId.PublishSingleFile => State.PublishSingleFile,
        PublishOptionId.UseAppHost => State.UseAppHost,
        PublishOptionId.CreateMacAppBundle => State.CreateMacAppBundle,
        PublishOptionId.CreateWindowsExecutable => State.CreateWindowsExecutable,
        PublishOptionId.BuildIpa => State.BuildIpa,
        PublishOptionId.ArchiveOnBuild => State.ArchiveOnBuild,
        PublishOptionId.RunAotCompilation => State.RunAotCompilation,
        PublishOptionId.EnableProfiledAot => State.EnableProfiledAot,
        PublishOptionId.CreateMappingFile => State.CreateMappingFile,
        PublishOptionId.EnableMultiDex => State.EnableMultiDex,
        PublishOptionId.UseAapt2 => State.UseAapt2,
        PublishOptionId.EnableDesugar => State.EnableDesugar,
        PublishOptionId.IncludeApk => State.IncludeApk,
        PublishOptionId.IncludeAab => State.IncludeAab,
        PublishOptionId.AndroidLinkMode => !string.Equals(State.AndroidLinkMode, "None", StringComparison.Ordinal),
        PublishOptionId.AndroidLinkTool => !string.IsNullOrWhiteSpace(State.AndroidLinkTool),
        PublishOptionId.AndroidDexTool => !string.IsNullOrWhiteSpace(State.AndroidDexTool),
        _ => false,
    };

    /// <summary>True when the option's current value satisfies a set of accepted values.</summary>
    public bool Satisfies(PublishOptionId option, IReadOnlyList<string> acceptedValues)
    {
        if (acceptedValues.Count == 0)
        {
            return IsOn(option);
        }

        var value = ValueOf(option);
        return value is not null && acceptedValues.Contains(value, StringComparer.Ordinal);
    }

    /// <summary>The option's current value as text, for choice options.</summary>
    public string? ValueOf(PublishOptionId option) => option switch
    {
        PublishOptionId.AndroidLinkMode => State.AndroidLinkMode,
        PublishOptionId.AndroidLinkTool => State.AndroidLinkTool,
        PublishOptionId.AndroidDexTool => State.AndroidDexTool,
        _ => null,
    };

    /// <summary>Turns a toggle option on or off.</summary>
    public void Set(PublishOptionId option, bool value)
    {
        if (IsOn(option) == value)
        {
            return;
        }

        State = SetToggle(option, value);
    }

    /// <summary>
    /// Turns a choice option on by moving it to its first accepted value. Returns true when the
    /// value actually moved.
    /// </summary>
    public bool SetChoice(PublishOptionId option, IReadOnlyList<string> acceptedValues)
    {
        if (acceptedValues.Count == 0)
        {
            return false;
        }

        var first = acceptedValues[0];
        if (string.Equals(ValueOf(option), first, StringComparison.Ordinal))
        {
            return false;
        }

        State = SetValue(option, first);
        return true;
    }

    /// <summary>Turns an option off and records it, so the resolution never turns it back on.</summary>
    public void Disable(PublishOptionId option)
    {
        Set(option, false);
        _disabledDuringResolution.Add(option);
    }

    private PublishSettingsState SetToggle(PublishOptionId option, bool value) => option switch
    {
        PublishOptionId.SelfContained => State with { SelfContained = value },
        PublishOptionId.PublishTrimmed => State with { PublishTrimmed = value },
        PublishOptionId.PublishAot => State with { PublishAot = value },
        PublishOptionId.PublishReadyToRun => State with { PublishReadyToRun = value },
        PublishOptionId.PublishSingleFile => State with { PublishSingleFile = value },
        PublishOptionId.UseAppHost => State with { UseAppHost = value },
        PublishOptionId.CreateMacAppBundle => State with { CreateMacAppBundle = value },
        PublishOptionId.CreateWindowsExecutable => State with { CreateWindowsExecutable = value },
        PublishOptionId.BuildIpa => State with { BuildIpa = value },
        PublishOptionId.ArchiveOnBuild => State with { ArchiveOnBuild = value },
        PublishOptionId.RunAotCompilation => State with { RunAotCompilation = value },
        PublishOptionId.EnableProfiledAot => State with { EnableProfiledAot = value },
        PublishOptionId.CreateMappingFile => State with { CreateMappingFile = value },
        PublishOptionId.EnableMultiDex => State with { EnableMultiDex = value },
        PublishOptionId.UseAapt2 => State with { UseAapt2 = value },
        PublishOptionId.EnableDesugar => State with { EnableDesugar = value },
        PublishOptionId.IncludeApk => State with { IncludeApk = value },
        PublishOptionId.IncludeAab => State with { IncludeAab = value },
        _ => State,
    };

    private PublishSettingsState SetValue(PublishOptionId option, string value) => option switch
    {
        PublishOptionId.AndroidLinkMode => State with { AndroidLinkMode = value },
        PublishOptionId.AndroidLinkTool => State with { AndroidLinkTool = value },
        PublishOptionId.AndroidDexTool => State with { AndroidDexTool = value },
        _ => State,
    };
}
