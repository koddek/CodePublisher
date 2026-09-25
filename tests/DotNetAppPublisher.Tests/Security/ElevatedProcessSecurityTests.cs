using DotNetAppPublisher.Features.Shared.Process;

namespace DotNetAppPublisher.Tests.Security;

public sealed class ElevatedProcessSecurityTests
{
    [Test]
    [Arguments("DOTNET_APP_PUBLISHER_KEYSTORE_PASSWORD")]
    [Arguments("DOTNET_APP_PUBLISHER_API_KEY")]
    public async Task EnsureSafeEnvironment_RejectsSecretVariables(string variableName)
    {
        var environment = new Dictionary<string, string>
        {
            [variableName] = "do-not-write-me"
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            ElevatedProcessSecurity.EnsureSafeEnvironment(environment));

        await Assert.That(exception.Message).Contains(variableName);
        await Assert.That(exception.Message).DoesNotContain("do-not-write-me");
    }

    [Test]
    public void EnsureSafeEnvironment_AllowsNonSecretVariables()
    {
        ElevatedProcessSecurity.EnsureSafeEnvironment(new Dictionary<string, string>
        {
            ["DOTNET_ROOT"] = "/usr/local/share/dotnet",
            ["DOTNET_MULTILEVEL_LOOKUP"] = "0"
        });
    }

    [Test]
    public async Task WriteOwnerOnly_CreatesExecutableFileWithoutGroupOrOtherPermissions_WhenUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(Path.GetTempPath(), $"dnap-permission-test-{Guid.NewGuid():N}.txt");
        try
        {
            await ElevatedProcessSecurity.WriteOwnerOnlyAsync(
                path,
                "test",
                executable: true,
                CancellationToken.None);

            var mode = File.GetUnixFileMode(path);
            var groupOrOther = mode &
                (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                 UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);

            using (Assert.Multiple())
            {
                await Assert.That(groupOrOther).IsEqualTo(UnixFileMode.None);
                await Assert.That(mode & UnixFileMode.UserExecute).IsEqualTo(UnixFileMode.UserExecute);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
}
