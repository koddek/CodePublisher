using System.Windows.Input;
using CodePublisher.ViewModels;

namespace CodePublisher.Tests.Views;

/// <summary>
/// The device-options list in the wireless panel binds a button's Command to the option, so the action has
/// to be an ICommand: binding a Func&lt;Task&gt; there fails at runtime with "Command is expected" and leaves
/// the button dead.
/// </summary>
public sealed class WirelessDeviceOptionTests
{
    [Test]
    public async Task InvokeCommand_IsAnICommandAndRunsTheAction()
    {
        var ran = 0;
        var option = new WirelessDeviceOption(
            "Disconnect",
            "End the live debugging session. The device stays paired.",
            () =>
            {
                ran++;
                return Task.CompletedTask;
            },
            isDestructive: true);

        var command = option.InvokeCommand;

        using (Assert.Multiple())
        {
            await Assert.That(command).IsAssignableTo<ICommand>();
            await Assert.That(option.Label).IsEqualTo("Disconnect");
            await Assert.That(option.IsDestructive).IsTrue();
            await Assert.That(command.CanExecute(null)).IsTrue();
        }

        command.Execute(null);
        await Assert.That(ran).IsEqualTo(1);
    }

    [Test]
    public async Task InvokeCommand_AwaitsAnAsynchronousAction()
    {
        var completed = false;
        var option = new WirelessDeviceOption(
            "Unpair (delete completely)",
            "Remove the pairing and all saved configuration for this device.",
            async () =>
            {
                await Task.Delay(10);
                completed = true;
            },
            isDestructive: true);

        await option.InvokeCommand.ExecuteAsync(null);

        await Assert.That(completed).IsTrue();
    }
}
