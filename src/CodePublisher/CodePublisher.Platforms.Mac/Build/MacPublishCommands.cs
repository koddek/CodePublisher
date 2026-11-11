using CodePublisher.Features.Publishing.Build;
using CodePublisher.Models;

namespace CodePublisher.Features.Publishing.Build;

/// <summary>
/// The macOS desktop publish command. Mac Catalyst is an Apple-toolchain platform and is dispatched
/// to the Apple library at the composition root instead; this builder covers the plain
/// <c>net*-osx-*</c> desktop target.
/// </summary>
public static class MacPublishCommands
{
    public static List<string> BuildCommand(
        string dotnetPath,
        PublishConfiguration configuration,
        string projectFilePath,
        string? customTrimProperty)
    {
        var runtimeIdentifier = configuration.RuntimeIdentifier.Trim();
        var isNativeOsxRid = runtimeIdentifier.StartsWith("osx-", StringComparison.OrdinalIgnoreCase);

        var command = PublishCommandHelpers.Start(dotnetPath, "publish", projectFilePath,
            configuration.TargetFramework, configuration.Configuration, runtimeIdentifier);
        PublishCommandHelpers.AddBooleanProperty(command, "SelfContained", configuration.SelfContained);

        PublishCommandHelpers.AddTrimArgument(command, customTrimProperty, configuration.PublishTrimmed);
        PublishCommandHelpers.AddBooleanProperty(command, "PublishReadyToRun", configuration.PublishReadyToRun && !configuration.PublishAot);
        PublishCommandHelpers.AddBooleanProperty(command, "PublishSingleFile", configuration.PublishSingleFile && !configuration.PublishAot);
        // PublishAot is honored only on native osx RIDs; the Windows/Linux RIDs take the same flag in their own builders.
        PublishCommandHelpers.AddBooleanProperty(command, "PublishAot", configuration.PublishAot && isNativeOsxRid);

        if (configuration.UseAppHost)
        {
            command.Add("-p:UseAppHost=true");
        }

        return command;
    }
}
