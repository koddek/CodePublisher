using System.Diagnostics;

namespace DotNetAppPublisher.Features.Publishing.Process;

public static class ProcessRunner
{
    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        Action<string> writeOutput,
        CancellationToken cancellationToken,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        if (arguments.Count == 0 || string.IsNullOrWhiteSpace(arguments[0]))
        {
            throw new ArgumentException("At least one process argument is required.", nameof(arguments));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = arguments[0],
            WorkingDirectory = GetSafeWorkingDirectory(workingDirectory),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        foreach (var argument in arguments.Skip(1))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new System.Diagnostics.Process { StartInfo = startInfo };
        process.Start();

        var standardOutputTask = PumpReaderAsync(process.StandardOutput, writeOutput, cancellationToken);
        var standardErrorTask = PumpReaderAsync(process.StandardError, writeOutput, cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            writeOutput(Environment.NewLine + "Cancelling running process..." + Environment.NewLine);
            KillProcessTree(process);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(standardOutputTask, standardErrorTask);
            throw;
        }

        await Task.WhenAll(standardOutputTask, standardErrorTask);
        return process.ExitCode;
    }

    public static string GetSafeWorkingDirectory(string? preferredDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(preferredDirectory) && Directory.Exists(preferredDirectory))
        {
            return preferredDirectory;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home) && Directory.Exists(home))
        {
            return home;
        }

        return Path.GetTempPath();
    }

    private static async Task PumpReaderAsync(StreamReader reader, Action<string> writeOutput, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            writeOutput(line + Environment.NewLine);
        }
    }

    private static void KillProcessTree(System.Diagnostics.Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }
}
