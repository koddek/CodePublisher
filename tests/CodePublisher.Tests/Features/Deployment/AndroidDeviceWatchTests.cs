using CodePublisher.Features.Deployment.Android;

namespace CodePublisher.Tests.Features.Deployment;

public sealed class AndroidDeviceWatchTests
{
    private static AndroidDeviceInfo Phone(
        string serial = "R58M12345",
        string name = "Pixel 8",
        string state = AndroidDeviceState.Ready,
        string connection = AndroidDeviceConnection.Usb) =>
        new(serial, name, "device", IsRunning: true, state, connection, "Pixel 8", "16", "36", "arm64-v8a");

    private static AndroidDeviceInfo Emulator(
        string serial = "emulator-5554",
        string name = "Medium_Phone",
        string state = AndroidDeviceState.Ready) =>
        new(serial, name, "emulator", IsRunning: true, state, AndroidDeviceConnection.Emulator, name, "17", "37", "arm64-v8a");

    private static AndroidDeviceInfo StoppedAvd(string name = "Tablet_API_35") =>
        new(string.Empty, name, "emulator", IsRunning: false, AndroidDeviceState.Offline, AndroidDeviceConnection.Emulator);

    [Test]
    public async Task Fingerprint_IgnoresTargetsThatAreNotRunning()
    {
        var withAvd = AndroidDeviceWatch.Fingerprint([Phone(), StoppedAvd()]);
        var withoutAvd = AndroidDeviceWatch.Fingerprint([Phone()]);

        await Assert.That(withAvd).IsEqualTo(withoutAvd);
    }

    [Test]
    public async Task Fingerprint_DoesNotDependOnTheOrderOfTheList()
    {
        await Assert.That(AndroidDeviceWatch.Fingerprint([Phone(), Emulator()]))
            .IsEqualTo(AndroidDeviceWatch.Fingerprint([Emulator(), Phone()]));
    }

    [Test]
    public async Task Fingerprint_ChangesWhenADeviceStateChanges()
    {
        var ready = AndroidDeviceWatch.Fingerprint([Phone()]);
        var unauthorized = AndroidDeviceWatch.Fingerprint([Phone(state: AndroidDeviceState.Unauthorized)]);

        await Assert.That(ready).IsNotEqualTo(unauthorized);
    }

    [Test]
    public async Task NeedsRefresh_IsFalseWhileAdbAgreesWithTheVisibleList()
    {
        var probe = new List<AndroidDeviceListEntry>
        {
            new("emulator-5554", "device", true, false, "sdk_gphone16k_arm64", "sdk_gphone16k_arm64", null)
        };
        var shown = AndroidDeviceWatch.Fingerprint([Emulator(), StoppedAvd()]);

        await Assert.That(AndroidDeviceWatch.NeedsRefresh(shown, probe)).IsFalse();
    }

    [Test]
    public async Task NeedsRefresh_IsTrueWhenADeviceIsPluggedInOrUnplugged()
    {
        var seeded = AndroidDeviceWatch.Fingerprint([Emulator()]);
        var probeWithPhone = new List<AndroidDeviceListEntry>
        {
            new("emulator-5554", "device", true, false, null, null, null),
            new("R58M12345", "device", false, false, null, "Pixel 8", null)
        };

        using (Assert.Multiple())
        {
            await Assert.That(AndroidDeviceWatch.NeedsRefresh(seeded, probeWithPhone)).IsTrue();
            await Assert.That(AndroidDeviceWatch.NeedsRefresh(seeded, [])).IsTrue();
        }
    }

    [Test]
    public async Task NeedsRefresh_IsTrueWhenAnEmulatorFinishesBooting()
    {
        var booting = AndroidDeviceWatch.Fingerprint([Emulator(state: AndroidDeviceState.Offline)]);
        var probe = new List<AndroidDeviceListEntry>
        {
            new("emulator-5554", "device", true, false, null, null, null)
        };

        await Assert.That(AndroidDeviceWatch.NeedsRefresh(booting, probe)).IsTrue();
    }

    [Test]
    public async Task Compare_SplitsNewlyReadyFromNewlyAppearedAndGone()
    {
        var before = new List<AndroidDeviceInfo>
        {
            Phone(state: AndroidDeviceState.Unauthorized),
            Emulator(),
            Phone("R58M99999", "Old phone")
        };
        var after = new List<AndroidDeviceInfo>
        {
            Phone(),
            Emulator()
        };

        var change = AndroidDeviceWatch.Compare(before, after);

        using (Assert.Multiple())
        {
            await Assert.That(change.BecameReady.Count).IsEqualTo(1);
            await Assert.That(change.BecameReady[0].Serial).IsEqualTo("R58M12345");
            await Assert.That(change.Removed.Count).IsEqualTo(1);
            await Assert.That(change.Removed[0].Serial).IsEqualTo("R58M99999");
            await Assert.That(change.Added.Count).IsEqualTo(0);
            await Assert.That(change.HasChanges).IsTrue();
        }
    }

    [Test]
    public async Task Compare_ReportsNoChangeForTheSameTargets()
    {
        var devices = new List<AndroidDeviceInfo> { Phone(), Emulator(), StoppedAvd() };

        var change = AndroidDeviceWatch.Compare(devices, [Phone(), Emulator(), StoppedAvd()]);

        using (Assert.Multiple())
        {
            await Assert.That(change.HasChanges).IsFalse();
            await Assert.That(change.Added.Count).IsEqualTo(0);
            await Assert.That(change.BecameReady.Count).IsEqualTo(0);
            await Assert.That(change.Removed.Count).IsEqualTo(0);
        }
    }

    [Test]
    public async Task ChooseLaunchedTarget_PicksTheNewlyReadyDevice()
    {
        var appeared = AndroidDeviceWatch.ChooseLaunchedTarget(
            [Emulator(name: "Pixel_API_35"), Emulator(name: "Medium_Phone")],
            knownSerials: [],
            expectedAvdName: "Medium_Phone");

        await Assert.That(appeared!.Name).IsEqualTo("Medium_Phone");
    }

    [Test]
    public async Task ChooseLaunchedTarget_UsesTheSerialTheProbeSaw()
    {
        var appeared = AndroidDeviceWatch.ChooseLaunchedTarget(
            [Emulator(serial: "emulator-5554", name: "Pixel_API_35"), Emulator(serial: "emulator-5556", name: "Medium_Phone")],
            knownSerials: [],
            expectedAvdName: null,
            preferredSerial: "emulator-5556");

        await Assert.That(appeared!.Serial).IsEqualTo("emulator-5556");
    }

    [Test]
    public async Task ChooseLaunchedTarget_IgnoresKnownAndUnusableTargets()
    {
        var known = new[] { "emulator-5554" };

        using (Assert.Multiple())
        {
            await Assert.That(AndroidDeviceWatch.ChooseLaunchedTarget([Emulator()], known, "Medium_Phone")).IsNull();
            await Assert.That(AndroidDeviceWatch.ChooseLaunchedTarget(
                [Emulator(serial: "emulator-5556", state: AndroidDeviceState.Offline)],
                known,
                "Medium_Phone")).IsNull();
            await Assert.That(AndroidDeviceWatch.ChooseLaunchedTarget([StoppedAvd()], known, "Medium_Phone")).IsNull();
        }
    }

    [Test]
    public async Task DescribeChange_NamesWhatHappened()
    {
        var connectedUsb = AndroidDeviceWatch.DescribeChange(AndroidDeviceWatch.Compare([], [Phone()]));
        var emulatorOnline = AndroidDeviceWatch.DescribeChange(AndroidDeviceWatch.Compare([], [Emulator()]));
        var unplugged = AndroidDeviceWatch.DescribeChange(AndroidDeviceWatch.Compare([Phone()], []));
        var authorized = AndroidDeviceWatch.DescribeChange(
            AndroidDeviceWatch.Compare([Phone(state: AndroidDeviceState.Unauthorized)], [Phone()]));

        using (Assert.Multiple())
        {
            await Assert.That(connectedUsb).IsEqualTo("Pixel 8 (R58M12345) connected over USB.");
            await Assert.That(emulatorOnline).IsEqualTo("Medium_Phone (emulator-5554) is online.");
            await Assert.That(unplugged).IsEqualTo("Pixel 8 (R58M12345) disconnected.");
            await Assert.That(authorized).IsEqualTo("Pixel 8 (R58M12345) is ready.");
        }
    }

    [Test]
    public async Task DescribeChange_ExplainsATargetThatAppearedButCannotBeUsedYet()
    {
        var message = AndroidDeviceWatch.DescribeChange(
            AndroidDeviceWatch.Compare([], [Phone(state: AndroidDeviceState.Unauthorized)]));

        await Assert.That(message).Contains("approve the USB debugging prompt");
    }

    [Test]
    public async Task DescribeChange_IsNullWhenNothingChanged()
    {
        await Assert.That(AndroidDeviceWatch.DescribeChange(AndroidDeviceChange.None)).IsNull();
    }
}
