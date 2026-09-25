using DotNetAppPublisher.Features.Publishing.Configure;
using DotNetAppPublisher.Models;

namespace DotNetAppPublisher.Features.Publishing.Build;

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
            PublishPlatforms.Android => BuildAndroidCommand(dotnetPath, configuration, projectFilePath, customTrimProperty),
            PublishPlatforms.MacOs => BuildMacOsCommand(dotnetPath, configuration, projectFilePath, customTrimProperty),
            PublishPlatforms.Windows => BuildWindowsCommand(dotnetPath, configuration, projectFilePath, customTrimProperty),
            PublishPlatforms.Ios => BuildIosCommand(dotnetPath, configuration, projectFilePath, customTrimProperty),
            PublishPlatforms.Linux => BuildLinuxCommand(dotnetPath, configuration, projectFilePath, customTrimProperty),
            _ => throw new InvalidOperationException($"Unknown publish platform `{configuration.PublishPlatform}`.")
        };

        if (IsAndroidPlatform(configuration.PublishPlatform))
        {
            var formats = GetSelectedFormats(configuration);
            if (formats.Count == 0)
            {
                throw new InvalidOperationException("Select at least one package format: APK, AAB, or both.");
            }

            command.Add($"-p:AndroidPackageFormats={string.Join("%3B", formats)}");
        }

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
        if (IsAndroidPlatform(configuration.PublishPlatform))
        {
            return BuildAndroidBaselineCommand(dotnetPath, configuration, projectFilePath, outputDirectory, customTrimProperty);
        }

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
            "-p:PublishReadyToRun=false"
        };

        if (IsMacOsPlatform(configuration.PublishPlatform))
        {
            var isMacCatalyst = PlatformProfiles.IsMacCatalyst(
                configuration.TargetFramework,
                configuration.RuntimeIdentifier);
            var isNativeOsxRid = configuration.RuntimeIdentifier.StartsWith("osx-", StringComparison.OrdinalIgnoreCase);

            if (isMacCatalyst)
            {
                command.Add("-p:PublishTrimmed=true");
                command.Add("-p:PublishSingleFile=false");
                command.Add("-p:MtouchLink=None");
            }
            else
            {
                AddTrimArgument(command, customTrimProperty, false);
            }

            if (isNativeOsxRid)
            {
                command.Add("-p:UseAppHost=true");
                command.Add("-p:PublishSingleFile=true");
            }
        }
        else if (IsIosPlatform(configuration.PublishPlatform))
        {
            var isSimulatorRuntime = configuration.RuntimeIdentifier.StartsWith("iossimulator-", StringComparison.OrdinalIgnoreCase);
            command.Add("-p:UseAppHost=false");
            command.Add("-p:PublishTrimmed=true");
            command.Add("-p:PublishSingleFile=false");

            if (isSimulatorRuntime)
            {
                command[1] = "build";
            }
        }
        else
        {
            command.Add("-p:UseAppHost=true");
            AddTrimArgument(command, customTrimProperty, false);
            command.Add("-p:PublishSingleFile=true");
        }

        command.Add("-o");
        command.Add(outputDirectory);
        return command;
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

    private static List<string> BuildAndroidCommand(
        string dotnetPath,
        PublishConfiguration configuration,
        string projectFilePath,
        string? customTrimProperty)
    {
        var linkMode = configuration.AndroidLinkMode.Trim();
        var linkingEnabled = !string.Equals(linkMode, "None", StringComparison.Ordinal);

        if (configuration.RunAotCompilation && !linkingEnabled)
        {
            throw new InvalidOperationException(
                "RunAOTCompilation requires linking. Set Android Link Mode to SdkOnly or Full, or disable RunAOT.");
        }

        if (configuration.RunAotCompilation && !configuration.PublishTrimmed)
        {
            throw new InvalidOperationException(
                "RunAOTCompilation requires PublishTrimmed=true. Enable trimming or disable RunAOT.");
        }

        var androidToolError = PublishOptionPolicy.AndroidToolValidationMessage(
            configuration.AndroidLinkTool,
            configuration.AndroidDexTool);
        if (!string.IsNullOrWhiteSpace(androidToolError))
        {
            throw new InvalidOperationException(androidToolError);
        }

        if (configuration.EnableProfiledAot && !configuration.RunAotCompilation)
        {
            throw new InvalidOperationException(
                "Profiled AOT requires RunAOT. Enable RunAOT or disable Profiled AOT.");
        }

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
            $"-p:SelfContained={ToLowerInvariant(configuration.SelfContained)}",
            "-p:PublishAot=false",
            "-p:PublishReadyToRun=false",
            "-p:PublishSingleFile=false",
            $"-p:AndroidLinkMode={linkMode}",
            $"-p:RunAOTCompilation={ToLowerInvariant(configuration.RunAotCompilation)}",
            $"-p:AndroidEnableProfiledAot={ToLowerInvariant(configuration.EnableProfiledAot)}"
        };

        AddTrimArgument(command, customTrimProperty, configuration.PublishTrimmed);

        if (!string.Equals(linkMode, "None", StringComparison.Ordinal))
        {
            command.Add($"-p:AndroidLinkTool={configuration.AndroidLinkTool.Trim()}");
            command.Add($"-p:AndroidCreateProguardMappingFile={ToLowerInvariant(configuration.CreateMappingFile)}");
        }

        command.Add($"-p:AndroidDexTool={configuration.AndroidDexTool.Trim()}");

        if (configuration.EnableMultiDex)
        {
            command.Add("-p:AndroidEnableMultiDex=true");
        }

        if (!configuration.UseAapt2)
        {
            command.Add("-p:AndroidUseAapt2=false");
        }

        if (!configuration.EnableDesugar)
        {
            command.Add("-p:AndroidEnableDesugar=false");
        }

        switch (configuration.SignMode.Trim())
        {
            case "Sign":
                ValidateSigning(configuration);
                command.Add("-p:AndroidKeyStore=true");
                command.Add($"-p:AndroidSigningKeyStore={configuration.KeystorePath.Trim()}");
                command.Add($"-p:AndroidSigningKeyAlias={configuration.KeyAlias.Trim()}");
                command.Add("-p:AndroidSigningStorePass=$(DOTNET_APP_PUBLISHER_KEYSTORE_PASSWORD)");
                command.Add("-p:AndroidSigningKeyPass=$(DOTNET_APP_PUBLISHER_KEY_PASSWORD)");
                break;
            case "Do Not Sign":
                command.Add("-p:AndroidKeyStore=false");
                break;
        }

        return command;
    }

    private static List<string> BuildMacOsCommand(
        string dotnetPath,
        PublishConfiguration configuration,
        string projectFilePath,
        string? customTrimProperty)
    {
        var targetFramework = configuration.TargetFramework.Trim();
        var runtimeIdentifier = configuration.RuntimeIdentifier.Trim();
        var isMacCatalyst = PlatformProfiles.IsMacCatalyst(targetFramework, runtimeIdentifier);
        var isNativeOsxRid = runtimeIdentifier.StartsWith("osx-", StringComparison.OrdinalIgnoreCase);

        var command = new List<string>
        {
            dotnetPath,
            "publish",
            projectFilePath,
            "-f",
            targetFramework,
            "-c",
            configuration.Configuration.Trim(),
            "-r",
            runtimeIdentifier,
            $"-p:SelfContained={ToLowerInvariant(configuration.SelfContained)}"
        };

        if (isMacCatalyst)
        {
            command.Add("-p:PublishTrimmed=true");
            command.Add("-p:PublishAot=false");
            command.Add("-p:PublishReadyToRun=false");
            command.Add("-p:PublishSingleFile=false");
            if (!configuration.PublishTrimmed)
            {
                command.Add("-p:MtouchLink=None");
            }

            if (!string.IsNullOrWhiteSpace(customTrimProperty))
            {
                command.Add($"-p:{customTrimProperty}={ToLowerInvariant(configuration.PublishTrimmed)}");
            }
        }
        else
        {
            AddTrimArgument(command, customTrimProperty, configuration.PublishTrimmed);
        }

        if (!isMacCatalyst)
        {
            AddBooleanProperty(command, "PublishReadyToRun", configuration.PublishReadyToRun && !configuration.PublishAot);
            AddBooleanProperty(command, "PublishSingleFile", configuration.PublishSingleFile && !configuration.PublishAot);
            AddBooleanProperty(command, "PublishAot", configuration.PublishAot && isNativeOsxRid);
        }

        if (!isMacCatalyst && configuration.UseAppHost)
        {
            command.Add("-p:UseAppHost=true");
        }

        return command;
    }

    private static List<string> BuildWindowsCommand(
        string dotnetPath,
        PublishConfiguration configuration,
        string projectFilePath,
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
            $"-p:SelfContained={ToLowerInvariant(configuration.SelfContained)}",
            $"-p:UseAppHost={ToLowerInvariant(configuration.CreateWindowsExecutable)}"
        };

        AddTrimArgument(command, customTrimProperty, configuration.PublishTrimmed);
        AddBooleanProperty(command, "PublishReadyToRun", configuration.PublishReadyToRun && !configuration.PublishAot);
        AddBooleanProperty(command, "PublishSingleFile", configuration.PublishSingleFile && !configuration.PublishAot);
        AddBooleanProperty(command, "PublishAot", configuration.PublishAot);

        return command;
    }

    private static List<string> BuildLinuxCommand(
        string dotnetPath,
        PublishConfiguration configuration,
        string projectFilePath,
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
            $"-p:SelfContained={ToLowerInvariant(configuration.SelfContained)}",
            $"-p:UseAppHost={ToLowerInvariant(configuration.UseAppHost)}"
        };

        AddTrimArgument(command, customTrimProperty, configuration.PublishTrimmed);
        AddBooleanProperty(command, "PublishReadyToRun", configuration.PublishReadyToRun && !configuration.PublishAot);
        AddBooleanProperty(command, "PublishSingleFile", configuration.PublishSingleFile && !configuration.PublishAot);
        AddBooleanProperty(command, "PublishAot", configuration.PublishAot);

        return command;
    }

    private static List<string> BuildIosCommand(
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

    private static IReadOnlyList<string> BuildAndroidBaselineCommand(
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
            "-p:AndroidLinkMode=None",
            "-p:AndroidLinkTool=r8",
            "-p:AndroidDexTool=d8",
            "-p:PublishAot=false",
            "-p:PublishReadyToRun=false",
            "-p:PublishSingleFile=false",
            "-p:RunAOTCompilation=false",
            "-p:AndroidEnableProfiledAot=false",
            "-p:AndroidPackageFormats=apk",
            "-o",
            outputDirectory
        };

        AddTrimArgument(command, customTrimProperty, false);
        return command;
    }

    private static void AddTrimArgument(List<string> command, string? customTrimProperty, bool value)
    {
        var text = ToLowerInvariant(value);
        command.Add($"-p:PublishTrimmed={text}");
        if (!string.IsNullOrWhiteSpace(customTrimProperty))
        {
            command.Add($"-p:{customTrimProperty}={text}");
        }
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

    private static void AddBooleanProperty(List<string> command, string propertyName, bool value)
        => command.Add($"-p:{propertyName}={ToLowerInvariant(value)}");

    private static void ValidateSigning(PublishConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.KeystorePath))
        {
            throw new InvalidOperationException("Signing mode is Sign, but no keystore file was selected.");
        }

        if (string.IsNullOrWhiteSpace(configuration.KeyAlias))
        {
            throw new InvalidOperationException("Signing mode is Sign, but the key alias is empty.");
        }

        if (string.IsNullOrWhiteSpace(configuration.KeystorePassword))
        {
            throw new InvalidOperationException("Signing mode is Sign, but the store password is empty.");
        }

        if (string.IsNullOrWhiteSpace(configuration.KeyPassword))
        {
            throw new InvalidOperationException("Signing mode is Sign, but the key password is empty.");
        }
    }

    private static IReadOnlyList<string> GetSelectedFormats(PublishConfiguration configuration)
    {
        var formats = new List<string>();
        if (configuration.IncludeAab)
        {
            formats.Add("aab");
        }

        if (configuration.IncludeApk)
        {
            formats.Add("apk");
        }

        return formats;
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

    private static string ToLowerInvariant(bool value) => value ? "true" : "false";

    private static bool IsAndroidPlatform(string value) => string.Equals(value, PublishPlatforms.Android, StringComparison.Ordinal);

    private static bool IsMacOsPlatform(string value) => string.Equals(value, PublishPlatforms.MacOs, StringComparison.Ordinal);

    private static bool IsIosPlatform(string value) => string.Equals(value, PublishPlatforms.Ios, StringComparison.Ordinal);
}
