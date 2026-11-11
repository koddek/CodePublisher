using CodePublisher.Features.Publishing.Inspect;
using CodePublisher.Models;

namespace CodePublisher.Features.Publishing.Build;

/// <summary>
/// The Apple publish commands: iOS (device and simulator) and Mac Catalyst. Catalyst shares the
/// Apple SDK and the forced-trimming behavior of iOS but publishes through the macOS host entry,
/// so it has its own commands here and the composition root dispatches the macOS-with-Catalyst
/// platform entry to them.
///
/// Trimming cannot be turned off on the Apple workload: <c>PublishTrimmed=false</c> is a build
/// error there. To disable linking, set <c>TrimMode=copy</c> (the successor of the deprecated
/// <c>MtouchLink=None</c>).
/// </summary>
public static class ApplePublishCommands
{
    public static List<string> BuildIosCommand(
        string dotnetPath,
        PublishConfiguration configuration,
        string projectFilePath,
        string? customTrimProperty)
    {
        var runtimeIdentifier = configuration.RuntimeIdentifier.Trim();
        var isSimulatorRuntime = runtimeIdentifier.StartsWith("iossimulator-", StringComparison.OrdinalIgnoreCase);
        var command = new List<string>
        {
            dotnetPath,
            isSimulatorRuntime ? "build" : "publish",
            projectFilePath,
            "-f",
            configuration.TargetFramework.Trim(),
            "-c",
            configuration.Configuration.Trim(),
            "-r",
            runtimeIdentifier,
            $"-p:SelfContained={ToLowerInvariant(configuration.SelfContained)}",
            "-p:UseAppHost=false",
            "-p:PublishTrimmed=true",
            "-p:PublishAot=false",
            "-p:PublishReadyToRun=false",
            "-p:PublishSingleFile=false"
        };

        if (!string.IsNullOrWhiteSpace(customTrimProperty))
        {
            command.Add($"-p:{customTrimProperty}=true");
        }

        if (configuration.ArchiveOnBuild && !isSimulatorRuntime)
        {
            command.Add("-p:ArchiveOnBuild=true");
        }

        if (configuration.BuildIpa && !isSimulatorRuntime)
        {
            command.Add("-p:BuildIpa=true");
        }

        return command;
    }

    /// <summary>
    /// Mac Catalyst: the Apple SDK in release clothing. Trimming is forced unless the project
    /// opts out with <c>TrimMode=copy</c>, archiving is the .ipa-equivalent path, and the desktop
    /// compiler flags (ReadyToRun, NativeAOT, single-file) never apply.
    /// </summary>
    public static List<string> BuildCatalystCommand(
        string dotnetPath,
        PublishConfiguration configuration,
        string projectFilePath,
        string? customTrimProperty)
    {
        var command = BuildAppleBase(dotnetPath, configuration, projectFilePath);

        command.Add("-p:PublishTrimmed=true");
        command.Add("-p:PublishAot=false");
        command.Add("-p:PublishReadyToRun=false");
        command.Add("-p:PublishSingleFile=false");
        if (!configuration.PublishTrimmed)
        {
            command.Add("-p:TrimMode=copy");
        }

        if (!string.IsNullOrWhiteSpace(customTrimProperty))
        {
            command.Add($"-p:{customTrimProperty}={ToLowerInvariant(configuration.PublishTrimmed)}");
        }

        return command;
    }

    public static List<string> BuildIosBaselineCommand(
        string dotnetPath,
        PublishConfiguration configuration,
        string projectFilePath,
        string outputDirectory,
        string? customTrimProperty)
    {
        var runtimeIdentifier = configuration.RuntimeIdentifier.Trim();
        var isSimulatorRuntime = runtimeIdentifier.StartsWith("iossimulator-", StringComparison.OrdinalIgnoreCase);

        var command = BuildDesktopBaselineStart(dotnetPath, configuration, projectFilePath);
        command.Add("-p:UseAppHost=false");
        command.Add("-p:PublishTrimmed=true");
        command.Add("-p:PublishSingleFile=false");

        if (!string.IsNullOrWhiteSpace(customTrimProperty))
        {
            command.Add($"-p:{customTrimProperty}=true");
        }

        if (isSimulatorRuntime)
        {
            command[1] = "build";
        }

        command.Add("-o");
        command.Add(outputDirectory);
        return command;
    }

    public static List<string> BuildCatalystBaselineCommand(
        string dotnetPath,
        PublishConfiguration configuration,
        string projectFilePath,
        string outputDirectory,
        string? customTrimProperty)
    {
        var command = BuildDesktopBaselineStart(dotnetPath, configuration, projectFilePath);
        command.Add("-p:PublishTrimmed=true");
        command.Add("-p:PublishSingleFile=false");
        command.Add("-p:TrimMode=copy");

        command.Add("-o");
        command.Add(outputDirectory);
        return command;
    }

    private static List<string> BuildAppleBase(
        string dotnetPath,
        PublishConfiguration configuration,
        string projectFilePath)
        =>
        [
            dotnetPath,
            "publish",
            projectFilePath,
            "-f",
            configuration.TargetFramework.Trim(),
            "-c",
            configuration.Configuration.Trim(),
            "-r",
            configuration.RuntimeIdentifier.Trim(),
            $"-p:SelfContained={ToLowerInvariant(configuration.SelfContained)}"
        ];

    private static List<string> BuildDesktopBaselineStart(
        string dotnetPath,
        PublishConfiguration configuration,
        string projectFilePath)
        =>
        [
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
            "-p:PublishReadyToRun=false"
        ];

    private static string ToLowerInvariant(bool value) => value ? "true" : "false";
}
