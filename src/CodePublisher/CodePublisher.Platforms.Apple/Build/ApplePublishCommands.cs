using CodePublisher.Features.Publishing.Inspect;
using CodePublisher.Models;

namespace CodePublisher.Features.Publishing.Build;

/// <summary>
/// The Apple publish commands: iOS (device and simulator) and Mac Catalyst. Catalyst shares the
/// Apple SDK and the forced-trimming behavior of iOS but publishes through the macOS host entry,
/// so it has its own commands here and the composition root dispatches the macOS-with-Catalyst
/// platform entry to them.
///
/// The .NET 11 Apple workload runs CoreCLR and owns both trimming and ReadyToRun. Setting
/// <c>PublishTrimmed</c> to any value is an error there, and <c>PublishReadyToRun=false</c> would
/// turn off the whole-app precompilation the SDK enables by default. On .NET 11 the commands omit
/// both properties and let the workload decide. On .NET 10 the Mono-era properties still apply, and
/// <c>MtouchLink=None</c> is the documented way to relax linking.
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
            "-p:PublishAot=false",
            "-p:PublishSingleFile=false"
        };

        AddTrimmingAndReadyToRun(command, configuration);

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
    /// Mac Catalyst: the Apple SDK in release clothing. Trimming and ReadyToRun follow the
    /// workload defaults on .NET 11; the desktop compiler flags (NativeAOT, single-file) never
    /// apply.
    /// </summary>
    public static List<string> BuildCatalystCommand(
        string dotnetPath,
        PublishConfiguration configuration,
        string projectFilePath,
        string? customTrimProperty)
    {
        var command = BuildAppleBase(dotnetPath, configuration, projectFilePath);

        command.Add("-p:PublishAot=false");
        command.Add("-p:PublishSingleFile=false");
        AddTrimmingAndReadyToRun(command, configuration);

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

        var command = BuildAppleBaselineStart(dotnetPath, configuration, projectFilePath);
        command.Add("-p:UseAppHost=false");
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
        var command = BuildAppleBaselineStart(dotnetPath, configuration, projectFilePath);
        command.Add("-p:PublishSingleFile=false");
        if (ProjectInspector.GetTargetFrameworkVersion(configuration.TargetFramework) < 11)
        {
            command.Add("-p:TrimMode=copy");
        }

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

    private static List<string> BuildAppleBaselineStart(
        string dotnetPath,
        PublishConfiguration configuration,
        string projectFilePath)
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
            "-p:PublishAot=false"
        };

        // .NET 11 Apple precompiles with ReadyToRun by default; passing false would disable it.
        if (ProjectInspector.GetTargetFrameworkVersion(configuration.TargetFramework) < 11)
        {
            command.Add("-p:PublishReadyToRun=false");
        }

        return command;
    }

    /// <summary>
    /// Adds the trimming and ReadyToRun switches that differ by target framework. On .NET 11
    /// neither property may be set: the workload owns both. On .NET 10 the Mono-era flags apply.
    /// </summary>
    private static void AddTrimmingAndReadyToRun(List<string> command, PublishConfiguration configuration)
    {
        if (ProjectInspector.GetTargetFrameworkVersion(configuration.TargetFramework) >= 11)
        {
            if (!configuration.PublishTrimmed)
            {
                command.Add("-p:MtouchLink=None");
            }

            return;
        }

        command.Add("-p:PublishTrimmed=true");
        command.Add("-p:PublishReadyToRun=false");
        if (!configuration.PublishTrimmed)
        {
            command.Add("-p:TrimMode=copy");
        }
    }

    private static string ToLowerInvariant(bool value) => value ? "true" : "false";
}
