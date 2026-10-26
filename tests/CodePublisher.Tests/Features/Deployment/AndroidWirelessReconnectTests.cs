using CodePublisher.Features.Deployment.Android;

namespace CodePublisher.Tests.Features.Deployment;

public sealed class AndroidWirelessReconnectTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 14, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyDictionary<string, DateTimeOffset> NoAttempts =
        new Dictionary<string, DateTimeOffset>();
    private static readonly IReadOnlyDictionary<string, int> NoFailures = new Dictionary<string, int>();

    [Test]
    [Arguments(0, 15)]
    [Arguments(1, 20)]
    [Arguments(2, 30)]
    [Arguments(3, 60)]
    [Arguments(40, 60)]
    public async Task RetryDelay_BacksOffAndStopsGrowing(int failures, int expectedSeconds)
    {
        await Assert.That(AndroidWirelessReconnect.RetryDelay(failures).TotalSeconds).IsEqualTo(expectedSeconds);
    }

    [Test]
    public async Task ChooseHost_RetriesAPairedPhoneThatIsNotConnected() =>
        await Assert.That(Choose(
                pairedHosts: new Dictionary<string, string?> { ["192.168.50.237"] = "192.168.50.237:42857" },
                readyHosts: [],
                attempts: NoAttempts,
                failures: NoFailures))
            .IsEqualTo("192.168.50.237");

    [Test]
    public async Task ChooseHost_SkipsAPhoneAdbAlreadyListsAsUsable()
    {
        var host = Choose(
            pairedHosts: new Dictionary<string, string?> { ["192.168.50.237"] = "192.168.50.237:42857" },
            readyHosts: ["192.168.50.237"],
            attempts: NoAttempts,
            failures: NoFailures);

        await Assert.That(host).IsNull();
    }

    [Test]
    public async Task ChooseHost_LeavesAPhoneAloneUntilItsRetryWindowOpens()
    {
        var pairedHosts = new Dictionary<string, string?> { ["192.168.50.237"] = null };

        using (Assert.Multiple())
        {
            await Assert.That(Choose(
                    pairedHosts,
                    [],
                    new Dictionary<string, DateTimeOffset> { ["192.168.50.237"] = Now.AddSeconds(-5) },
                    NoFailures)).IsNull();
            await Assert.That(Choose(
                    pairedHosts,
                    [],
                    new Dictionary<string, DateTimeOffset> { ["192.168.50.237"] = Now.AddSeconds(-16) },
                    NoFailures)).IsEqualTo("192.168.50.237");
        }
    }

    [Test]
    public async Task ChooseHost_PrefersAPhoneThatWasNeverTried()
    {
        var host = Choose(
            pairedHosts: new Dictionary<string, string?>
            {
                ["192.168.50.99"] = null,
                ["192.168.50.237"] = null
            },
            readyHosts: [],
            attempts: new Dictionary<string, DateTimeOffset>
            {
                // Failed a while ago, but the second phone has not been attempted at all.
                ["192.168.50.99"] = Now.AddSeconds(-120)
            },
            failures: new Dictionary<string, int> { ["192.168.50.99"] = 2 });

        await Assert.That(host).IsEqualTo("192.168.50.237");
    }

    [Test]
    public async Task ChooseHost_TakesTurnsBetweenTwoPhones()
    {
        var pairedHosts = new Dictionary<string, string?>
        {
            ["192.168.50.99"] = null,
            ["192.168.50.237"] = null
        };
        var attempts = new Dictionary<string, DateTimeOffset>
        {
            ["192.168.50.99"] = Now.AddSeconds(-120),
            ["192.168.50.237"] = Now.AddSeconds(-90)
        };

        var first = Choose(pairedHosts, [], attempts, NoFailures);
        attempts["192.168.50.99"] = Now;
        var second = Choose(pairedHosts, [], attempts, NoFailures);

        using (Assert.Multiple())
        {
            await Assert.That(first).IsEqualTo("192.168.50.99");
            await Assert.That(second).IsEqualTo("192.168.50.237");
        }
    }

    [Test]
    public async Task ChooseHost_WaitsLongerAfterRepeatedFailures()
    {
        var pairedHosts = new Dictionary<string, string?> { ["192.168.50.237"] = null };
        var failures = new Dictionary<string, int> { ["192.168.50.237"] = 3 };

        using (Assert.Multiple())
        {
            // Three failures mean a 60 s window, so an attempt 30 s ago is too soon.
            await Assert.That(Choose(
                    pairedHosts,
                    [],
                    new Dictionary<string, DateTimeOffset> { ["192.168.50.237"] = Now.AddSeconds(-30) },
                    failures)).IsNull();
            await Assert.That(Choose(
                    pairedHosts,
                    [],
                    new Dictionary<string, DateTimeOffset> { ["192.168.50.237"] = Now.AddSeconds(-61) },
                    failures)).IsEqualTo("192.168.50.237");
        }
    }

    [Test]
    public async Task ChooseHost_IgnoresBlankHosts() =>
        await Assert.That(Choose(
                pairedHosts: new Dictionary<string, string?> { ["   "] = null },
                readyHosts: [],
                attempts: NoAttempts,
                failures: NoFailures))
            .IsNull();

    private static string? Choose(
        IReadOnlyDictionary<string, string?> pairedHosts,
        IReadOnlyCollection<string> readyHosts,
        IReadOnlyDictionary<string, DateTimeOffset> attempts,
        IReadOnlyDictionary<string, int> failures) =>
        AndroidWirelessReconnect.ChooseHost(pairedHosts, readyHosts, attempts, failures, Now);
}
