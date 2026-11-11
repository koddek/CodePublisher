using CodePublisher.Features.Publishing.Build;
using CodePublisher.Models;

namespace CodePublisher.Features.Publishing.Build;

/// <summary>
/// The Windows publish command. The launcher switch is the Windows-specific piece: a
/// <c>.exe</c> host is produced when "Create Windows executable" is on.
/// </summary>
public static class WindowsPublishCommands
{
    public static List<string> BuildCommand(
        string dotnetPath,
        PublishConfiguration configuration,
        string projectFilePath,
        string? customTrimProperty)
    {
        var command = PublishCommandHelpers.Start(dotnetPath, "publish", projectFilePath,
            configuration.TargetFramework, configuration.Configuration, configuration.RuntimeIdentifier);
        PublishCommandHelpers.AddBooleanProperty(command, "SelfContained", configuration.SelfContained);
        PublishCommandHelpers.AddBooleanProperty(command, "UseAppHost", configuration.CreateWindowsExecutable);

        PublishCommandHelpers.AddTrimArgument(command, customTrimProperty, configuration.PublishTrimmed);
        PublishCommandHelpers.AddBooleanProperty(command, "PublishReadyToRun", configuration.PublishReadyToRun && !configuration.PublishAot);
        PublishCommandHelpers.AddBooleanProperty(command, "PublishSingleFile", configuration.PublishSingleFile && !configuration.PublishAot);
        PublishCommandHelpers.AddBooleanProperty(command, "PublishAot", configuration.PublishAot);

        return command;
    }
}
