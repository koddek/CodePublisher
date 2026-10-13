using DotNetAppPublisher.Features.Deployment.Ios;

namespace DotNetAppPublisher.Tests.Features.Deployment;

public sealed class IosStopParsersTests
{
    private const string BundlePath = "/private/var/containers/Bundle/Application/9F2C1D4E-1111-2222-3333-444455556666/Demo.app";

    private const string AppsJson = """
        {
          "info": { "commandType": "devicectl.device.info.apps", "outcome": "success" },
          "result": {
            "apps": [
              {
                "bundleIdentifier": "com.example.other",
                "bundlePath": "/private/var/containers/Bundle/Application/AAAAAAAA-0000-0000-0000-000000000000/Other.app"
              },
              {
                "bundleIdentifier": "com.example.demo",
                "bundlePath": "/private/var/containers/Bundle/Application/9F2C1D4E-1111-2222-3333-444455556666/Demo.app"
              }
            ]
          }
        }
        """;

    private const string ProcessesJson = """
        {
          "info": { "commandType": "devicectl.device.info.processes", "outcome": "success" },
          "result": {
            "runningProcesses": [
              { "name": "Demo", "processIdentifier": 4312, "executable": "/private/var/containers/Bundle/Application/9F2C1D4E-1111-2222-3333-444455556666/Demo.app/Demo" },
              { "name": "Demo helper", "processIdentifier": 4318, "executable": "/private/var/containers/Bundle/Application/9F2C1D4E-1111-2222-3333-444455556666/Demo.app/XPC Services/Helper.app/Helper" },
              { "name": "SpringBoard", "processIdentifier": 42, "executable": "/System/Library/CoreServices/SpringBoard.app/SpringBoard" },
              { "name": "notourapp", "processIdentifier": 99, "executable": "/private/var/containers/Bundle/Application/AAAAAAAA-0000-0000-0000-000000000000/Other.app/Other" }
            ]
          }
        }
        """;

    [Test]
    public async Task FindAppBundlePath_ReturnsBundlePathForMatchingBundleId()
    {
        await Assert.That(IosStopParsers.FindAppBundlePath(AppsJson, "com.example.demo")).IsEqualTo(BundlePath);
    }

    [Test]
    public async Task FindAppBundlePath_IsCaseInsensitiveOnBundleId()
    {
        await Assert.That(IosStopParsers.FindAppBundlePath(AppsJson, "COM.EXAMPLE.DEMO")).IsEqualTo(BundlePath);
    }

    [Test]
    public async Task FindAppBundlePath_ReturnsNullWhenBundleIdIsNotInstalled()
    {
        await Assert.That(IosStopParsers.FindAppBundlePath(AppsJson, "com.example.missing")).IsNull();
    }

    [Test]
    public async Task FindAppBundlePath_ReadsFileUrlAndPercentEncodedPaths()
    {
        const string json = """
            {
              "result": {
                "apps": [
                  {
                    "bundleIdentifier": "com.example.spaced",
                    "bundleURL": "file:///private/var/containers/Bundle/Application/BBBB-1111/My%20Demo.app"
                  }
                ]
              }
            }
            """;

        await Assert.That(IosStopParsers.FindAppBundlePath(json, "com.example.spaced"))
            .IsEqualTo("/private/var/containers/Bundle/Application/BBBB-1111/My Demo.app");
    }

    [Test]
    public async Task FindAppBundlePath_ReadsNestedPathObjects()
    {
        const string json = """
            {
              "result": {
                "apps": [
                  {
                    "bundleIdentifier": "com.example.nested",
                    "bundleURL": { "path": "/private/var/containers/Bundle/Application/CCCC-2222/Nested.app" }
                  }
                ]
              }
            }
            """;

        await Assert.That(IosStopParsers.FindAppBundlePath(json, "com.example.nested"))
            .IsEqualTo("/private/var/containers/Bundle/Application/CCCC-2222/Nested.app");
    }

    [Test]
    public async Task FindAppBundlePath_FallsBackToAnyAppBundleShapedString()
    {
        const string json = """
            {
              "result": {
                "applications": [
                  { "bundleId": "com.example.renamed", "someUnknownKey": "/var/containers/Bundle/Application/DDDD-3210/Renamed.app" }
                ]
              }
            }
            """;

        await Assert.That(IosStopParsers.FindAppBundlePath(json, "com.example.renamed"))
            .IsEqualTo("/var/containers/Bundle/Application/DDDD-3210/Renamed.app");
    }

    [Test]
    public async Task FindAppBundlePath_ToleratesMissingOrInvalidInput()
    {
        using (Assert.Multiple())
        {
            await Assert.That(IosStopParsers.FindAppBundlePath(null, "com.example.demo")).IsNull();
            await Assert.That(IosStopParsers.FindAppBundlePath("", "com.example.demo")).IsNull();
            await Assert.That(IosStopParsers.FindAppBundlePath("not json", "com.example.demo")).IsNull();
            await Assert.That(IosStopParsers.FindAppBundlePath("{}", "com.example.demo")).IsNull();
            await Assert.That(IosStopParsers.FindAppBundlePath(AppsJson, " ")).IsNull();
        }
    }

    [Test]
    public async Task FindRunningPids_ReturnsOnlyProcessesInsideTheBundleSorted()
    {
        var pids = IosStopParsers.FindRunningPids(ProcessesJson, BundlePath);

        using (Assert.Multiple())
        {
            await Assert.That(pids.Count).IsEqualTo(2);
            await Assert.That(pids[0]).IsEqualTo(4312);
            await Assert.That(pids[1]).IsEqualTo(4318);
        }
    }

    [Test]
    public async Task FindRunningPids_ToleratesTrailingSlashAndDifferentCasing()
    {
        var pids = IosStopParsers.FindRunningPids(ProcessesJson, BundlePath + "/");

        await Assert.That(pids.Count).IsEqualTo(2);
    }

    [Test]
    public async Task FindRunningPids_ReadsPidStringAndAlternateKeySpellings()
    {
        const string json = """
            {
              "result": {
                "processes": [
                  { "name": "Demo", "pid": "777", "executablePath": "/private/var/containers/Bundle/Application/9F2C1D4E-1111-2222-3333-444455556666/Demo.app/Demo" },
                  { "name": "Demo two", "processId": 778, "executable": { "path": "/private/var/containers/Bundle/Application/9F2C1D4E-1111-2222-3333-444455556666/Demo.app/Second" } }
                ]
              }
            }
            """;

        var pids = IosStopParsers.FindRunningPids(json, BundlePath);

        using (Assert.Multiple())
        {
            await Assert.That(pids.Count).IsEqualTo(2);
            await Assert.That(pids[0]).IsEqualTo(777);
            await Assert.That(pids[1]).IsEqualTo(778);
        }
    }

    [Test]
    public async Task FindRunningPids_ReturnsEmptyWhenNothingMatchesOrInputIsBad()
    {
        using (Assert.Multiple())
        {
            await Assert.That(IosStopParsers.FindRunningPids(ProcessesJson, null).Count).IsEqualTo(0);
            await Assert.That(IosStopParsers.FindRunningPids(ProcessesJson, "/private/var/other/Nope.app").Count).IsEqualTo(0);
            await Assert.That(IosStopParsers.FindRunningPids("not json", BundlePath).Count).IsEqualTo(0);
            await Assert.That(IosStopParsers.FindRunningPids(null, BundlePath).Count).IsEqualTo(0);
        }
    }

    [Test]
    public async Task LooksLikeNotRunning_RecognisesNoOpFailuresOnly()
    {
        using (Assert.Multiple())
        {
            await Assert.That(IosStopParsers.LooksLikeNotRunning("Failed to find process for application")).IsTrue();
            await Assert.That(IosStopParsers.LooksLikeNotRunning("The operation couldn't be completed. (not running)")).IsTrue();
            await Assert.That(IosStopParsers.LooksLikeNotRunning("No such process")).IsTrue();
            await Assert.That(IosStopParsers.LooksLikeNotRunning("Application is not installed on the device")).IsTrue();
            await Assert.That(IosStopParsers.LooksLikeNotRunning("Unable to find bundle identifier")).IsTrue();

            await Assert.That(IosStopParsers.LooksLikeNotRunning("Device is locked")).IsFalse();
            await Assert.That(IosStopParsers.LooksLikeNotRunning("")).IsFalse();
            await Assert.That(IosStopParsers.LooksLikeNotRunning(null)).IsFalse();
        }
    }
}
