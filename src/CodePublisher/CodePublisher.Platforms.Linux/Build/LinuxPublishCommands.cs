using CodePublisher.Features.Publishing.Build;
using CodePublisher.Models;

namespace CodePublisher.Features.Publishing.Build;

/// <summary>
/// The Linux publish command. Linux takes the plain application-host switch, with no launcher
/// rename and no bundle step.
/// </summary>
public static class LinuxPublishCommands
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
        PublishCommandHelpers.AddBooleanProperty(command, "UseAppHost", configuration.UseAppHost);

        PublishCommandHelpers.AddTrimArgument(command, customTrimProperty, configuration.PublishTrimmed);
        PublishCommandHelpers.AddBooleanProperty(command, "PublishReadyToRun", configuration.PublishReadyToRun && !configuration.PublishAot);
        PublishCommandHelpers.AddBooleanProperty(command, "PublishSingleFile", configuration.PublishSingleFile && !configuration.PublishAot);
        PublishCommandHelpers.AddBooleanProperty(command, "PublishAot", configuration.PublishAot);

        return command;
    }
}
