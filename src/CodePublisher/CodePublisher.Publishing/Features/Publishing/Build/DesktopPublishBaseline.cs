using CodePublisher.Features.Publishing.Build;
using CodePublisher.Models;

namespace CodePublisher.Features.Publishing.Configure;

/// <summary>
/// The baseline publish command shared by the native desktop platforms (macOS without Catalyst,
/// Windows, Linux): self-contained, no AOT, no ReadyToRun, application host, trimming off. It is
/// the command the app recommends when an optimized publish fails.
/// </summary>
public static class DesktopPublishBaseline
{
    public static List<string> Build(
        string dotnetPath,
        PublishConfiguration configuration,
        string projectFilePath,
        string outputDirectory,
        string? customTrimProperty)
    {
        var command = new List<string>
        {
            dotnetPath,
            "publish",
            projectFilePath,
            "-f",
            configuration.TargetFramework.Trim(),
            "-c",
            configuration.Configuration.Trim(),
            "-r",
            configuration.RuntimeIdentifier.Trim(),
            "-p:SelfContained=true",
            "-p:PublishAot=false",
            "-p:PublishReadyToRun=false",
            "-p:UseAppHost=true",
            "-p:PublishSingleFile=true"
        };

        PublishCommandHelpers.AddTrimArgument(command, customTrimProperty, false);
        command.Add("-o");
        command.Add(outputDirectory);
        return command;
    }
}
