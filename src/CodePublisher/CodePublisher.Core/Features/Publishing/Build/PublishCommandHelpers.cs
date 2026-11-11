namespace CodePublisher.Features.Publishing.Build;

/// <summary>
/// The shared plumbing of the per-platform publish command builders: the command list start, the
/// boolean property emission, and the trim switch. Each platform library builds its own argument
/// list; these helpers keep the mechanics identical across them.
/// </summary>
public static class PublishCommandHelpers
{
    public static List<string> Start(string dotnetPath, string verb, string projectFilePath, string targetFramework, string configuration, string? runtimeIdentifier)
    {
        var command = new List<string>
        {
            dotnetPath,
            verb,
            projectFilePath,
            "-f",
            targetFramework.Trim(),
            "-c",
            configuration.Trim()
        };

        if (!string.IsNullOrWhiteSpace(runtimeIdentifier))
        {
            command.Add("-r");
            command.Add(runtimeIdentifier.Trim());
        }

        return command;
    }

    public static void AddBooleanProperty(List<string> command, string propertyName, bool value)
        => command.Add($"-p:{propertyName}={ToLowerInvariant(value)}");

    public static void AddTrimArgument(List<string> command, string? customTrimProperty, bool value)
    {
        var text = ToLowerInvariant(value);
        command.Add($"-p:PublishTrimmed={text}");
        if (!string.IsNullOrWhiteSpace(customTrimProperty))
        {
            command.Add($"-p:{customTrimProperty}={text}");
        }
    }

    public static string ToLowerInvariant(bool value) => value ? "true" : "false";
}
