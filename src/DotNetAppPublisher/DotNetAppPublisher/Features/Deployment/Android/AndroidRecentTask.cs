namespace DotNetAppPublisher.Features.Deployment.Android;

/// <summary>
/// One entry of the device's Recents (app switcher), as `dumpsys activity recents` reports it.
/// <see cref="RootTaskId"/> is the id `am stack remove` accepts; <see cref="PackageName"/> is the owner
/// the entry was read from, so a close operation only ever dismisses a task that really belongs to the
/// package it was asked about.
/// </summary>
public sealed record AndroidRecentTask(
    int TaskId,
    int RootTaskId,
    string Type,
    string PackageName,
    string Component)
{
    /// <summary>Home and the Recents surface itself are never dismissed.</summary>
    public bool IsDismissable =>
        RootTaskId > 0
        && !string.Equals(Type, "home", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(Type, "recents", StringComparison.OrdinalIgnoreCase);

    public bool BelongsTo(string packageName) =>
        !string.IsNullOrWhiteSpace(packageName)
        && string.Equals(PackageName, packageName.Trim(), StringComparison.OrdinalIgnoreCase);

    public string DisplayLabel => Component.Length > 0 ? Component : PackageName;
}
