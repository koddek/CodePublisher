using CodePublisher.Features.Deployment.Ios;

namespace CodePublisher.Tests.Features.Deployment.CrashLogs;

public sealed class IosCrashLogParserTests
{
    [Test]
    public async Task ParseFileListing_ReadsTheFilesOfTheListing()
    {
        const string json =
            """
            {
              "result": {
                "files": [
                  { "name": "MyApp-2026-10-20.ips", "size": 4096, "modifiedDate": "2026-10-20T20:35:08Z" }
                ]
              }
            }
            """;

        var entries = IosCrashLogParser.ParseFileListing(json);

        using (Assert.Multiple())
        {
            await Assert.That(entries.Count).IsEqualTo(1);
            await Assert.That(entries[0].Name).IsEqualTo("MyApp-2026-10-20.ips");
            await Assert.That(entries[0].SizeBytes).IsEqualTo(4096);
            await Assert.That(entries[0].Modified!.Value.Year).IsEqualTo(2026);
        }
    }

    [Test]
    public async Task ParseFileListing_AcceptsTheOtherFieldNamesOtherXcodeVersionsWrite()
    {
        const string json =
            """
            {
              "result": {
                "fileListing": {
                  "items": [
                    { "fileName": "MyApp.ips", "fileSize": 12, "lastModified": "2026-10-20T20:35:08Z" }
                  ]
                }
              }
            }
            """;

        var entries = IosCrashLogParser.ParseFileListing(json);

        using (Assert.Multiple())
        {
            await Assert.That(entries.Count).IsEqualTo(1);
            await Assert.That(entries[0].Name).IsEqualTo("MyApp.ips");
            await Assert.That(entries[0].SizeBytes).IsEqualTo(12);
        }
    }

    [Test]
    public async Task ParseFileListing_IgnoresInputThatIsNotJson()
    {
        using (Assert.Multiple())
        {
            await Assert.That(IosCrashLogParser.ParseFileListing("not json").Count).IsEqualTo(0);
            await Assert.That(IosCrashLogParser.ParseFileListing(null).Count).IsEqualTo(0);
        }
    }

    [Test]
    public async Task ParseFileListing_SkipsTheNodesThatAreNotFiles()
    {
        const string json =
            """
            {
              "result": {
                "name": "systemCrashLogs",
                "files": [
                  { "name": "MyApp.ips", "size": 10 }
                ]
              }
            }
            """;

        var entries = IosCrashLogParser.ParseFileListing(json);

        await Assert.That(entries.Count).IsEqualTo(1);
    }
}
