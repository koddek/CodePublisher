using CodePublisher.Features.NuGet.Build;

namespace CodePublisher.Tests.Features.NuGet;

public sealed class NuGetCommandBuilderTests
{
    [Test]
    public async Task Pack_UsesProject_Configuration_AndOutput()
    {
        var arguments = NuGetCommandBuilder.BuildPackArguments(
            "/dotnet", "/proj/Lib.csproj", "Release", "/proj/nupkgs", null, includeSymbols: false);

        using (Assert.Multiple())
        {
            await Assert.That(arguments[0]).IsEqualTo("/dotnet");
            await Assert.That(arguments[1]).IsEqualTo("pack");
            await Assert.That(arguments).Contains("/proj/Lib.csproj");
            await Assert.That(arguments).Contains("Release");
            await Assert.That(arguments).Contains("/proj/nupkgs");
            await Assert.That(arguments).DoesNotContain("--include-symbols");
            await Assert.That(arguments.Any(a => a.StartsWith("-p:PackageVersion=", StringComparison.Ordinal))).IsFalse();
        }
    }

    [Test]
    public async Task Pack_WithVersionOverride_AndSymbols_AddsBothFlags()
    {
        var arguments = NuGetCommandBuilder.BuildPackArguments(
            "/dotnet", "/proj/Lib.csproj", "Debug", "/proj/nupkgs", "2.1.0-beta.1", includeSymbols: true);

        using (Assert.Multiple())
        {
            await Assert.That(arguments).Contains("-p:PackageVersion=2.1.0-beta.1");
            await Assert.That(arguments).Contains("--include-symbols");
            await Assert.That(arguments).Contains("Debug");
        }
    }

    [Test]
    public async Task Pack_NoBuild_AddsTheFlag_AndForceBuild_UsesNoIncremental()
    {
        using (Assert.Multiple())
        {
            await Assert.That(NuGetCommandBuilder.BuildPackArguments(
                    "/dotnet", "/p/Lib.csproj", "Release", "/p/nupkgs", null, false, noBuild: true))
                .Contains("--no-build");
            await Assert.That(string.Join(" ", NuGetCommandBuilder.BuildForceBuildArguments("/dotnet", "/p/Lib.csproj", "Release")))
                .IsEqualTo("/dotnet build /p/Lib.csproj -c Release --no-incremental");
        }
    }

    [Test]
    public async Task Push_ContainsSource_ApiKey_AndSkipDuplicate()
    {
        var arguments = NuGetCommandBuilder.BuildPushArguments(
            "/dotnet", "/proj/nupkgs/Lib.1.0.0.nupkg", "https://api.nuget.org/v3/index.json", "secret-key");

        using (Assert.Multiple())
        {
            await Assert.That(string.Join(" ", arguments.Skip(1).Take(2))).IsEqualTo("nuget push");
            await Assert.That(arguments).Contains("/proj/nupkgs/Lib.1.0.0.nupkg");
            await Assert.That(arguments).Contains("--skip-duplicate");
            await Assert.That(arguments).Contains("secret-key");
        }
    }

    [Test]
    public async Task Mask_HidesApiKeyValues_ButNotOtherArguments()
    {
        var push = NuGetCommandBuilder.BuildPushArguments("/dotnet", "/p/Lib.1.0.0.nupkg", "https://api.nuget.org/v3/index.json", "super-secret");
        var delete = NuGetCommandBuilder.BuildDeleteArguments("/dotnet", "Lib", "1.0.0", "https://api.nuget.org/v3/index.json", "super-secret");
        var preview = NuGetCommandBuilder.Mask(push) + Environment.NewLine + NuGetCommandBuilder.Mask(delete);

        using (Assert.Multiple())
        {
            await Assert.That(preview).DoesNotContain("super-secret");
            await Assert.That(preview).Contains("--api-key ********");
            await Assert.That(preview).Contains("/p/Lib.1.0.0.nupkg");
            await Assert.That(preview).Contains("1.0.0");
        }
    }

    [Test]
    public async Task Mask_QuotesPathsWithSpaces()
    {
        var arguments = NuGetCommandBuilder.BuildPackArguments(
            "/dotnet", "/Users/me/My Project/Lib.csproj", "Release", "/Users/me/My Project/nupkgs", null, false);

        var preview = NuGetCommandBuilder.Mask(arguments);

        await Assert.That(preview).Contains("\"/Users/me/My Project/Lib.csproj\"");
    }

    [Test]
    public async Task Delete_IsNonInteractive_AndTargetsIdAndVersion()
    {
        var arguments = NuGetCommandBuilder.BuildDeleteArguments(
            "/dotnet", "Lib", "1.0.0", "https://nuget.pkg.github.com/omoi/index.json", "key");

        using (Assert.Multiple())
        {
            await Assert.That(string.Join(" ", arguments.Skip(1).Take(2))).IsEqualTo("nuget delete");
            await Assert.That(arguments).Contains("Lib");
            await Assert.That(arguments).Contains("1.0.0");
            await Assert.That(arguments).Contains("--non-interactive");
            await Assert.That(arguments).Contains("https://nuget.pkg.github.com/omoi/index.json");
        }
    }

    [Test]
    public async Task TestFlow_RegistersLocalSource_ThenAddsPackageToConsumer_ThenBuilds()
    {
        string Join(IReadOnlyList<string> args) => string.Join(" ", args);

        using (Assert.Multiple())
        {
            await Assert.That(Join(NuGetCommandBuilder.BuildAddLocalSourceArguments("/dotnet", "/proj/nupkgs")))
                .IsEqualTo($"/dotnet nuget add source /proj/nupkgs --name {NuGetCommandBuilder.LocalTestSourceName}");
            await Assert.That(Join(NuGetCommandBuilder.BuildRemoveLocalSourceArguments("/dotnet")))
                .IsEqualTo($"/dotnet nuget remove source {NuGetCommandBuilder.LocalTestSourceName}");
            await Assert.That(Join(NuGetCommandBuilder.BuildNewConsumerArguments("/dotnet", "/tmp/t/consumer")))
                .IsEqualTo("/dotnet new classlib -o /tmp/t/consumer");
            await Assert.That(Join(NuGetCommandBuilder.BuildAddPackageArguments("/dotnet", "/tmp/t/consumer/consumer.csproj", "Lib", "1.0.0", "/proj/nupkgs")))
                .IsEqualTo("/dotnet add /tmp/t/consumer/consumer.csproj package Lib --version 1.0.0 --source /proj/nupkgs");
            await Assert.That(Join(NuGetCommandBuilder.BuildConsumerArguments("/dotnet", "/tmp/t/consumer/consumer.csproj")))
                .IsEqualTo("/dotnet build /tmp/t/consumer/consumer.csproj -v q --nologo");
            await Assert.That(Join(NuGetCommandBuilder.BuildLocalsArguments("/dotnet")))
                .IsEqualTo("/dotnet nuget locals global-packages --list");
        }
    }
}
