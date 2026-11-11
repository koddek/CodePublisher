using CodePublisher.Features.Publishing.Configure;
using CodePublisher.Models;

namespace CodePublisher.Features.Publishing.Build;

/// <summary>
/// The publish command surface the app and the tests call: build the command for a platform,
/// build the baseline the app recommends when an optimized publish fails, and mask the
/// credentials it previews. The platform-specific argument lists live in each platform's library;
/// this class is the switchboard, so the engine never mixes one platform's flags into another's.
/// </summary>
public static class PublishCommandBuilder
{
    public static IReadOnlyList<string> Build(
        string dotnetPath,
        PublishConfiguration configuration,
        string projectFilePath,
        string outputDirectory,
        string? customTrimProperty)
    {
        ValidateOptions(configuration);

        var command = configuration.PublishPlatform switch
        {
            PublishPlatforms.Android => AndroidPublishCommands.BuildCommand(dotnetPath, configuration, projectFilePath, customTrimProperty),
            PublishPlatforms.MacOs => PublishPlatforms.IsMacCatalyst(configuration.TargetFramework, configuration.RuntimeIdentifier)
                ? ApplePublishCommands.BuildCatalystCommand(dotnetPath, configuration, projectFilePath, customTrimProperty)
                : MacPublishCommands.BuildCommand(dotnetPath, configuration, projectFilePath, customTrimProperty),
            PublishPlatforms.Windows => WindowsPublishCommands.BuildCommand(dotnetPath, configuration, projectFilePath, customTrimProperty),
            PublishPlatforms.Ios => ApplePublishCommands.BuildIosCommand(dotnetPath, configuration, projectFilePath, customTrimProperty),
            PublishPlatforms.Linux => LinuxPublishCommands.BuildCommand(dotnetPath, configuration, projectFilePath, customTrimProperty),
            _ => throw new InvalidOperationException($"Unknown publish platform `{configuration.PublishPlatform}`.")
        };

        command.Add("-o");
        command.Add(outputDirectory);
        return command;
    }

    public static IReadOnlyList<string> BuildBaseline(
        string dotnetPath,
        PublishConfiguration configuration,
        string projectFilePath,
        string outputDirectory,
        string? customTrimProperty)
    {
        return configuration.PublishPlatform switch
        {
            PublishPlatforms.Android =>
                AndroidPublishCommands.BuildBaselineCommand(dotnetPath, configuration, projectFilePath, outputDirectory, customTrimProperty),
            PublishPlatforms.MacOs => PublishPlatforms.IsMacCatalyst(configuration.TargetFramework, configuration.RuntimeIdentifier)
                ? ApplePublishCommands.BuildCatalystBaselineCommand(dotnetPath, configuration, projectFilePath, outputDirectory, customTrimProperty)
                : DesktopPublishBaseline.Build(dotnetPath, configuration, projectFilePath, outputDirectory, customTrimProperty),
            PublishPlatforms.Ios =>
                ApplePublishCommands.BuildIosBaselineCommand(dotnetPath, configuration, projectFilePath, outputDirectory, customTrimProperty),
            _ => DesktopPublishBaseline.Build(dotnetPath, configuration, projectFilePath, outputDirectory, customTrimProperty)
        };
    }

    public static string Mask(IEnumerable<string> arguments)
    {
        var masked = arguments.Select(argument =>
        {
            if (argument.StartsWith("-p:AndroidSigningStorePass=", StringComparison.Ordinal))
            {
                return "-p:AndroidSigningStorePass=********";
            }

            if (argument.StartsWith("-p:AndroidSigningKeyPass=", StringComparison.Ordinal))
            {
                return "-p:AndroidSigningKeyPass=********";
            }

            return argument;
        });

        return string.Join(" ", masked.Select(QuoteArgument));
    }

    private static void ValidateOptions(PublishConfiguration configuration)
    {
        if (configuration.PublishAot && !configuration.SelfContained)
        {
            throw new InvalidOperationException("Native AOT requires a self-contained publish.");
        }

        if (configuration.PublishAot && configuration.PublishReadyToRun)
        {
            throw new InvalidOperationException("Native AOT and ReadyToRun are mutually exclusive.");
        }

        if (configuration.PublishAot && configuration.PublishSingleFile)
        {
            throw new InvalidOperationException("Native AOT already produces a native executable; disable Single-file.");
        }

        if (configuration.PublishTrimmed
            && !configuration.SelfContained
            && !string.Equals(configuration.PublishPlatform, PublishPlatforms.Android, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Trimming requires a self-contained publish.");
        }

        if (configuration.PublishAot
            && !PublishOptionPolicy.IsPublishAotEnabled(
                configuration.PublishPlatform,
                configuration.TargetFramework,
                configuration.RuntimeIdentifier))
        {
            throw new InvalidOperationException("Native AOT is not supported for the selected platform or target framework.");
        }
    }

    private static string QuoteArgument(string argument)
    {
        if (string.IsNullOrEmpty(argument))
        {
            return "\"\"";
        }

        if (argument.All(character => !char.IsWhiteSpace(character) && character != '"' && character != '\''))
        {
            return argument;
        }

        return "\"" + argument.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }
}
