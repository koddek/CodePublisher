namespace CodePublisher.Features.Publishing.Configure;

/// <summary>
/// Applies the publish option graph to a settings snapshot.
///
/// The resolver is the one place that decides what a publish option set finally looks like. It
/// runs the platform rules and the dependency graph together until the result stops changing, so
/// the order in which the user clicked the options cannot change the outcome:
///   - a required companion is turned on, because the option that needs it is on;
///   - an option whose prerequisite the user turned off is turned off with it;
///   - two mutually exclusive options never stay on together;
///   - an optional companion is offered to the user instead of being forced.
///
/// A single <see cref="Resolve"/> call settles one snapshot and does not itself restore what an
/// earlier auto-change replaced. Cross-call restoration (deselecting a trigger removes its
/// contribution) is provided by <see cref="PublishOptionSession"/>, which re-derives effective
/// output from a clean user-intent baseline. Platform invariants applied by
/// <see cref="PublishSettingsPolicy.ApplyPlatformRules"/> are not restorable.
/// </summary>
public static class PublishOptionResolver
{
    private const int MaxPasses = 32;

    /// <summary>
    /// Resolves the option set for a publish platform. The result carries the resolved state, the
    /// changes that were applied, and the optional companions the user may still want to turn on.
    /// </summary>
    /// <param name="state">The options the user asked for.</param>
    /// <param name="changedOption">The option the user changed last, when the caller knows it.
    /// It settles a mutual exclusion in the user's favor and tells the resolver that a
    /// prerequisite was turned off on purpose.</param>
    /// <param name="changedValue">Whether the changed option was turned on or off.</param>
/// <param name="userDisabled">Options the user has explicitly turned off in earlier calls.</param>
/// <param name="conflictWinners">Options that win their active mutual-exclusion conflicts, so a
/// later unrelated edit or context change cannot reverse the user's choice.</param>
    public static PublishOptionResolution Resolve(
        PublishSettingsState state,
        PublishOptionId? changedOption = null,
        bool changedValue = true,
        IReadOnlyCollection<PublishOptionId>? userDisabled = null,
        IReadOnlyCollection<PublishOptionId>? conflictWinners = null)
    {
        var options = PublishOptionValues.From(state);
        if (userDisabled is not null)
        {
            foreach (var disabled in userDisabled)
            {
                options.MarkUserDisabled(disabled);
            }
        }

        if (changedOption is { } option)
        {
            // The user's change is applied to the snapshot before the graph runs, so the graph
            // sees what the user actually asked for.
            options.ApplyUserChange(option, changedValue);
            options.MarkUserChange(option, changedValue);
        }

        var changes = new List<PublishOptionChange>();
        var suggestions = new List<PublishOptionSuggestion>();

        // The platform rules and the dependency graph are applied together until the result
        // stops changing. A rule can only ever move an option one way, and an option turned off
        // during the walk is never turned back on, so the walk always settles.
        for (var pass = 0; pass < MaxPasses; pass++)
        {
            var before = options.State;

            // The dependency graph runs first. It knows which option the user turned off, so it
            // can switch off the options that relied on it. The platform rules then run on the
            // result, so they never re-enable something the user just turned off.
            foreach (var rule in PublishOptionGraph.RulesFor(state.PublishPlatform))
            {
                switch (rule.Relation)
                {
                    case PublishOptionRelation.Requires:
                        ApplyRequires(options, rule, changes);
                        break;

                    case PublishOptionRelation.ConflictsWith:
                        ApplyConflicts(options, rule, changes, conflictWinners);
                        break;

                    case PublishOptionRelation.Suggests:
                        CollectSuggestions(options, rule, suggestions);
                        break;
                }
            }

            options.State = PublishSettingsPolicy.ApplyPlatformRules(options.State);

            if (options.State == before)
            {
                break;
            }
        }

        return new PublishOptionResolution(options.State, Distinct(changes), suggestions);
    }

    /// <summary>
    /// Keeps the last change per option, so the report names each option once even when the
    /// walk touched it more than once.
    /// </summary>
    private static IReadOnlyList<PublishOptionChange> Distinct(IReadOnlyList<PublishOptionChange> changes)
    {
        var latest = new Dictionary<PublishOptionId, PublishOptionChange>();
        foreach (var change in changes)
        {
            latest[change.Option] = change;
        }

        return latest.Values.ToArray();
    }

    private static void ApplyRequires(
        PublishOptionValues options,
        PublishOptionRule rule,
        List<PublishOptionChange> changes)
    {
        if (!options.IsOn(rule.Source))
        {
            // The option that needs the target is off, so the target is free to be on or off.
            return;
        }

        if (options.Satisfies(rule.Target, rule.TargetValues))
        {
            return;
        }

        if (options.WasDisabledByUser(rule.Target) || options.WasDisabledDuringResolution(rule.Target))
        {
            // The prerequisite was turned off, so the option that depends on it has to go with
            // it. This is the "uncheck E, and everything that relies on E is unchecked" behavior.
            // A prerequisite that was turned off earlier in this walk counts too, so a chain of
            // dependents comes off together instead of switching each other back on.
            options.Disable(rule.Source);
            changes.Add(new PublishOptionChange(rule.Source, false, rule.Reason));
            return;
        }

        // The source needs the target, so turn the target on. This is the "toggle A, and A
        // automatically enables B" behavior.
        if (rule.TargetValues.Count > 0)
        {
            if (options.SetChoice(rule.Target, rule.TargetValues))
            {
                changes.Add(new PublishOptionChange(rule.Target, true, rule.Reason));
            }

            return;
        }

        if (PublishOptionValues.IsChoice(rule.Target))
        {
            // A choice target with no accepted values cannot be satisfied, so the source stays
            // off. Every choice rule in the graph names the values it accepts.
            options.Disable(rule.Source);
            changes.Add(new PublishOptionChange(rule.Source, false, rule.Reason));
            return;
        }

        options.Set(rule.Target, true);
        changes.Add(new PublishOptionChange(rule.Target, true, rule.Reason));
    }

    private static void ApplyConflicts(
        PublishOptionValues options,
        PublishOptionRule rule,
        List<PublishOptionChange> changes,
        IReadOnlyCollection<PublishOptionId>? conflictWinners)
    {
        if (!options.IsOn(rule.Source) || !options.IsOn(rule.Target))
        {
            return;
        }

        // The two cannot both stay on. A caller that persists conflict winners (a session)
        // gets a stable outcome across unrelated edits and context changes. Without that, the
        // option the user changed last wins; when the caller did not say, the target gives way,
        // which keeps the stronger option (native AOT) in charge and the weaker one
        // (ReadyToRun, single-file) out of the way.
        var turnedOff = conflictWinners switch
        {
            not null when conflictWinners.Contains(rule.Target) => rule.Source,
            not null when conflictWinners.Contains(rule.Source) => rule.Target,
            _ => options.WasChanged(rule.Target) ? rule.Source : rule.Target,
        };
        options.Disable(turnedOff);
        changes.Add(new PublishOptionChange(turnedOff, false, rule.Reason));
    }

    private static void CollectSuggestions(
        PublishOptionValues options,
        PublishOptionRule rule,
        List<PublishOptionSuggestion> suggestions)
    {
        if (!options.IsOn(rule.Source) || options.IsOn(rule.Target))
        {
            return;
        }

        if (suggestions.Any(suggestion => suggestion.Option == rule.Target))
        {
            return;
        }

        suggestions.Add(new PublishOptionSuggestion(rule.Target, rule.Reason));
    }
}
