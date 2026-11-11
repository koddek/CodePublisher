using CodePublisher.Features.Publishing.Configure;
using CodePublisher.Features.Publishing.Inspect;
using CodePublisher.Models;

namespace CodePublisher.Features.Publishing.Build;

/// <summary>
/// The Android publish command. Android is the only publish platform whose option set changes by
/// the target framework's major version: .NET 10 and earlier default to the Mono runtime (where
/// RunAOTCompilation and AndroidEnableProfiledAot exist), and .NET 11 removed the Mono path
/// entirely (CoreCLR is the default; RunAOT is rejected with XA1044, and NativeAOT is opt-in and
/// experimental). The builder therefore emits the Mono knobs only for pre-11 target frameworks, and
/// lets CoreCLR keep its default partial ReadyToRun for .NET 11.
/// </summary>
public static class AndroidPublishCommands
{
    public static List<string> BuildCommand(
        string dotnetPath,
        PublishConfiguration configuration,
        string projectFilePath,
        string? customTrimProperty)
    {
        var linkMode = configuration.AndroidLinkMode.Trim();
        var linkingEnabled = !string.Equals(linkMode, "None", StringComparison.Ordinal);
        var isMonoTarget = ProjectInspector.GetTargetFrameworkVersion(configuration.TargetFramework) < 11;

        if (configuration.RunAotCompilation && !isMonoTarget)
        {
            throw new InvalidOperationException(
                "RunAOTCompilation is a Mono option and is not supported by .NET 11 Android (CoreCLR). " +
                "Remove the RunAOT switch; .NET 11 compiles partial ReadyToRun when trimming is enabled. " +
                "See error XA1044 in the .NET for Android documentation.");
        }

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

        var androidToolError = AndroidPublishPolicy.AndroidToolValidationMessage(
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
            "-p:PublishSingleFile=false",
            $"-p:AndroidLinkMode={linkMode}"
        };

        if (isMonoTarget)
        {
            // Mono-only knobs. The .NET 11 SDK stops the build with XA1044 when either of these is
            // set at all — even to false — so they are emitted only for net10.0-android and earlier.
            command.Add($"-p:RunAOTCompilation={ToLowerInvariant(configuration.RunAotCompilation)}");
            command.Add($"-p:AndroidEnableProfiledAot={ToLowerInvariant(configuration.EnableProfiledAot)}");
            command.Add("-p:PublishReadyToRun=false");
        }

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

        var formats = GetSelectedFormats(configuration);
        if (formats.Count == 0)
        {
            throw new InvalidOperationException("Select at least one package format: APK, AAB, or both.");
        }

        command.Add($"-p:AndroidPackageFormats={string.Join("%3B", formats)}");
        return command;
    }

    public static List<string> BuildBaselineCommand(
        string dotnetPath,
        PublishConfiguration configuration,
        string projectFilePath,
        string outputDirectory,
        string? customTrimProperty)
    {
        var isMonoTarget = ProjectInspector.GetTargetFrameworkVersion(configuration.TargetFramework) < 11;

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
            "-p:PublishSingleFile=false",
            "-p:AndroidPackageFormats=apk"
        };

        if (isMonoTarget)
        {
            // The Mono knobs the analyzer check rejects on .NET 11 (XA1044). net10.0-android and
            // earlier still carry them so the baseline stays the documented Mono-safe profile.
            command.Add("-p:PublishReadyToRun=false");
            command.Add("-p:RunAOTCompilation=false");
            command.Add("-p:AndroidEnableProfiledAot=false");
        }

        AddTrimArgument(command, customTrimProperty, false);
        command.Add("-o");
        command.Add(outputDirectory);
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

    private static string ToLowerInvariant(bool value) => value ? "true" : "false";
}
