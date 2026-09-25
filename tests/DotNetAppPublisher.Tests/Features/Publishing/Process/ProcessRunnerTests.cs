using DotNetAppPublisher.Features.Publishing.Process;

namespace DotNetAppPublisher.Tests.Features.Publishing.Process;

public sealed class ProcessRunnerTests
{
    [Test]
    public async Task RunAsync_CapturesOutputAndExitCode()
    {
        var arguments = OperatingSystem.IsWindows()
            ? new[] { "cmd.exe", "/c", "echo process-runner-ok" }
            : new[] { "/bin/sh", "-c", "printf 'process-runner-ok\\n'" };
        var output = new List<string>();

        var exitCode = await ProcessRunner.RunAsync(
            arguments,
            output.Add,
            CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(string.Join(string.Empty, output)).Contains("process-runner-ok");
        }
    }

    [Test]
    public async Task GetSafeWorkingDirectory_UsesExistingDirectory()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"dnap-working-dir-{Guid.NewGuid():N}"));
        try
        {
            var result = ProcessRunner.GetSafeWorkingDirectory(directory.FullName);

            await Assert.That(result).IsEqualTo(directory.FullName);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task RunAsync_RejectsMissingExecutable()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            ProcessRunner.RunAsync(
                [],
                _ => { },
                CancellationToken.None).GetAwaiter().GetResult());

        await Assert.That(exception.Message).Contains("process argument");
    }
}
