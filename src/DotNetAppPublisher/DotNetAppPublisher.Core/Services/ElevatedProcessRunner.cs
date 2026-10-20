using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DotNetAppPublisher.Features.Shared.Process;

namespace DotNetAppPublisher.Services;

/// <summary>
/// Runs a process with administrator privileges on macOS via osascript — mirrors
/// Redth/MAUI.Sherpa ProcessExecutionService.ExecuteElevatedMacAsync.
/// No sudo -S, no SMJobBless. Windows uses Verb="runas".
/// </summary>
public static class ElevatedProcessRunner
{
    [DllImport("libc", EntryPoint = "access")]
    private static extern int UnixAccess(string pathname, int mode);
    private const int W_OK = 2;

    public static bool CanWriteDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                var probe = Path.Combine(path, $".write_test_{Guid.NewGuid():N}");
                File.WriteAllText(probe, string.Empty);
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        return UnixAccess(path, W_OK) == 0;
    }

    public static bool CanWriteToWorkloadLocation(string dotnetPath)
    {
        if (string.IsNullOrWhiteSpace(dotnetPath))
        {
            return false;
        }

        var installRoot = Path.GetDirectoryName(dotnetPath);
        if (string.IsNullOrWhiteSpace(installRoot))
        {
            return false;
        }

        // dotnet may be a symlink in /opt/homebrew/bin or /usr/local/bin — resolve to real install root
        try
        {
            var resolved = Path.GetFullPath(installRoot);
            // Walk up one more level if we are in a bin symlink dir: check both bin dir and typical /usr/local/share/dotnet
            var candidates = new List<string> { resolved };

            if (resolved.EndsWith("/bin", StringComparison.Ordinal))
            {
                var parent = Path.GetDirectoryName(resolved);
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    candidates.Add(parent);
                }
            }

            // Also check /usr/local/share/dotnet explicitly — that's where workloads are written even when dotnet is in /opt/homebrew/bin
            const string systemRoot = "/usr/local/share/dotnet";
            if (Directory.Exists(systemRoot))
            {
                candidates.Add(systemRoot);
            }

            // If any candidate is writable, no elevation needed. We check the most restrictive (systemRoot).
            // For the system install, check the install root + metadata subdirs like Sherpa does.
            var metadataRoot = Path.Combine(installRoot, "metadata");
            var workloadMetadata = Path.Combine(metadataRoot, "workloads");
            string statePath;
            if (Directory.Exists(workloadMetadata))
            {
                statePath = workloadMetadata;
            }
            else if (Directory.Exists(metadataRoot))
            {
                statePath = metadataRoot;
            }
            else
            {
                statePath = installRoot;
            }

            // If system root exists but is not writable, we need elevation regardless of bin symlink
            if (Directory.Exists(systemRoot) && !CanWriteDirectory(systemRoot))
            {
                return false;
            }

            return CanWriteDirectory(installRoot) && CanWriteDirectory(statePath);
        }
        catch
        {
            return CanWriteDirectory(installRoot);
        }
    }

    public static async Task<int> RunElevatedAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string>? environment,
        Action<string> writeOutput,
        CancellationToken cancellationToken)
    {
        ElevatedProcessSecurity.EnsureSafeEnvironment(environment);

        if (OperatingSystem.IsMacOS())
        {
            return await RunElevatedMacAsync(fileName, arguments, workingDirectory, environment, writeOutput, cancellationToken);
        }

        if (OperatingSystem.IsWindows())
        {
            return await RunElevatedWindowsAsync(fileName, arguments, workingDirectory, environment, writeOutput, cancellationToken);
        }

        // Linux / other: no GUI elevation — caller will show manual sudo guidance
        return -1;
    }

    private static string EscapeForShell(string value)
    {
        return "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }

    private static string EscapeForAppleScript(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static async Task<int> RunElevatedMacAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string>? environment,
        Action<string> writeOutput,
        CancellationToken cancellationToken)
    {
        var logFile = Path.Combine(Path.GetTempPath(), $"dnap_elevated_{Guid.NewGuid():N}.log");
        var scriptFile = Path.Combine(Path.GetTempPath(), $"dnap_elevated_{Guid.NewGuid():N}.sh");

        // Build command line with shell-escaped args
        var escapedArgs = arguments.Select(EscapeForShell);
        var commandLine = string.Join(" ", new[] { EscapeForShell(fileName) }.Concat(escapedArgs));

        var script = new StringBuilder();
        script.AppendLine("#!/bin/bash");
        script.AppendLine("set -o pipefail");
        script.AppendLine($"exec > >(tee -a {EscapeForShell(logFile)}) 2>&1");
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            script.AppendLine($"cd {EscapeForShell(workingDirectory)} || exit 1");
        }

        if (environment is not null)
        {
            foreach (var kv in environment)
            {
                script.AppendLine($"export {EscapeForShell(kv.Key)}={EscapeForShell(kv.Value)}");
            }
        }

        script.AppendLine(commandLine);
        script.AppendLine("EXIT_CODE=$?");
        script.AppendLine($"printf '\\n__EXIT_CODE__:%s\\n' \"$EXIT_CODE\" >> {EscapeForShell(logFile)}");

        await ElevatedProcessSecurity.WriteOwnerOnlyAsync(
            scriptFile,
            script.ToString(),
            executable: true,
            cancellationToken);
        await ElevatedProcessSecurity.WriteOwnerOnlyAsync(
            logFile,
            string.Empty,
            executable: false,
            cancellationToken);
        ElevatedProcessSecurity.RestrictToOwner(logFile, executable: false);
        ElevatedProcessSecurity.RestrictToOwner(scriptFile, executable: true);

        var escapedScript = EscapeForAppleScript(scriptFile);
        var osascriptCommand = $"do shell script \"\\\"{escapedScript}\\\"\" with administrator privileges";

        writeOutput("🔐 Requesting administrator privileges..." + Environment.NewLine);

        var startInfo = new ProcessStartInfo
        {
            FileName = "osascript",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add(osascriptCommand);

        using var process = new Process { StartInfo = startInfo };

        // Tail log file while osascript runs
        var tailCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var tailTask = TailLogFileAsync(logFile, writeOutput, tailCts.Token);

        string osascriptOutput = string.Empty;
        string osascriptError = string.Empty;

        try
        {
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var exitTask = process.WaitForExitAsync(cancellationToken);

            await Task.WhenAll(outputTask, errorTask, exitTask);
            osascriptOutput = await outputTask;
            osascriptError = await errorTask;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        finally
        {
            tailCts.Cancel();
            try { await tailTask; } catch { }

            // Cleanup script file (keep log until parsed)
            try { File.Delete(scriptFile); } catch { }
        }

        // Check for user cancel
        var combinedOsa = osascriptOutput + Environment.NewLine + osascriptError;
        if (combinedOsa.Contains("User canceled", StringComparison.OrdinalIgnoreCase)
            || combinedOsa.Contains("(-128)", StringComparison.Ordinal))
        {
            writeOutput("Workload restore cancelled by user." + Environment.NewLine);
            try { File.Delete(logFile); } catch { }
            return -128;
        }

        if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(File.ReadAllText(logFile)))
        {
            // osascript itself failed
            if (!string.IsNullOrWhiteSpace(osascriptError))
            {
                writeOutput(osascriptError + Environment.NewLine);
            }

            if (!string.IsNullOrWhiteSpace(osascriptOutput))
            {
                writeOutput(osascriptOutput + Environment.NewLine);
            }

            try { File.Delete(logFile); } catch { }
            return process.ExitCode;
        }

        // Parse real exit code from log marker
        int realExitCode = process.ExitCode;
        try
        {
            var logContent = await File.ReadAllTextAsync(logFile, cancellationToken);
            // Flush any remaining tail content that wasn't yet written
            // Tail already streamed, but ensure final chunk is output (tail polling may have missed last 100ms)
            // We already streamed via tail; no need to re-output, just parse marker
            var markerIdx = logContent.LastIndexOf("__EXIT_CODE__:", StringComparison.Ordinal);
            if (markerIdx >= 0)
            {
                var after = logContent[(markerIdx + "__EXIT_CODE__:".Length)..].Trim();
                var lineEnd = after.IndexOfAny(['\r', '\n']);
                if (lineEnd >= 0)
                {
                    after = after[..lineEnd].Trim();
                }

                if (int.TryParse(after, out var parsed))
                {
                    realExitCode = parsed;
                }
            }
        }
        catch
        {
            // use osascript exit code
        }
        finally
        {
            try { File.Delete(logFile); } catch { }
        }

        return realExitCode;
    }

    private static async Task TailLogFileAsync(string logFile, Action<string> writeOutput, CancellationToken cancellationToken)
    {
        long position = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!File.Exists(logFile))
                {
                    await Task.Delay(100, cancellationToken);
                    continue;
                }

                string newContent;
                try
                {
                    using var stream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    if (stream.Length <= position)
                    {
                        await Task.Delay(100, cancellationToken);
                        continue;
                    }

                    stream.Seek(position, SeekOrigin.Begin);
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    newContent = await reader.ReadToEndAsync(cancellationToken);
                    position = stream.Position;
                }
                catch (IOException)
                {
                    await Task.Delay(100, cancellationToken);
                    continue;
                }

                // Filter out the marker line from live output
                var lines = newContent.Split('\n');
                foreach (var line in lines)
                {
                    if (line.Contains("__EXIT_CODE__:", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (line.Length > 0)
                    {
                        writeOutput(line + Environment.NewLine);
                    }
                    else if (newContent.EndsWith("\n", StringComparison.Ordinal))
                    {
                        // preserve blank lines as they appear in the stream
                    }
                }

                await Task.Delay(100, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // tail cancelled — flush remaining content
            try
            {
                if (File.Exists(logFile))
                {
                    using var stream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    if (stream.Length > position)
                    {
                        stream.Seek(position, SeekOrigin.Begin);
                        using var reader = new StreamReader(stream, Encoding.UTF8);
                        var remaining = await reader.ReadToEndAsync(CancellationToken.None);
                        if (!string.IsNullOrWhiteSpace(remaining))
                        {
                            var filtered = remaining.Replace("__EXIT_CODE__:", string.Empty);
                            // Avoid double-output of marker; just flush non-marker lines
                            foreach (var line in filtered.Split('\n'))
                            {
                                if (line.Contains("__EXIT_CODE__:", StringComparison.Ordinal))
                                {
                                    continue;
                                }

                                if (!string.IsNullOrWhiteSpace(line))
                                {
                                    writeOutput(line + Environment.NewLine);
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // ignore flush errors
            }
        }
    }

    private static async Task<int> RunElevatedWindowsAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string>? environment,
        Action<string> writeOutput,
        CancellationToken cancellationToken)
    {
        var logFile = Path.Combine(Path.GetTempPath(), $"dnap_elevated_{Guid.NewGuid():N}.log");
        var scriptFile = Path.Combine(Path.GetTempPath(), $"dnap_elevated_{Guid.NewGuid():N}.ps1");

        var escapedArgs = arguments.Select(arg => $"\"{arg.Replace("\"", "`\"", StringComparison.Ordinal)}\"");
        var commandLine = $"{fileName} {string.Join(" ", escapedArgs)}";

        var ps = new StringBuilder();
        ps.AppendLine("$ErrorActionPreference = 'Continue'");
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            ps.AppendLine($"Set-Location -Path \"{workingDirectory.Replace("\"", "\"\"", StringComparison.Ordinal)}\"");
        }

        if (environment is not null)
        {
            foreach (var kv in environment)
            {
                ps.AppendLine($"$env:{kv.Key} = \"{kv.Value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"");
            }
        }

        ps.AppendLine($"& {commandLine} 2>&1 | Tee-Object -FilePath \"{logFile.Replace("\"", "\"\"", StringComparison.Ordinal)}\" -Append");
        ps.AppendLine("$ec = $LASTEXITCODE");
        ps.AppendLine($"Add-Content -Path \"{logFile.Replace("\"", "\"\"", StringComparison.Ordinal)}\" -Value \"__EXIT_CODE__:$ec\"");

        await File.WriteAllTextAsync(scriptFile, ps.ToString(), cancellationToken);
        await File.WriteAllTextAsync(logFile, string.Empty, cancellationToken);

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = true,
            Verb = "runas",
            CreateNoWindow = false
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptFile);

        var tailCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var tailTask = TailLogFileAsync(logFile, writeOutput, tailCts.Token);

        try
        {
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            await process.WaitForExitAsync(cancellationToken);
            tailCts.Cancel();
            try { await tailTask; } catch { }

            int realExitCode = process.ExitCode;
            try
            {
                var logContent = await File.ReadAllTextAsync(logFile, cancellationToken);
                var idx = logContent.LastIndexOf("__EXIT_CODE__:", StringComparison.Ordinal);
                if (idx >= 0)
                {
                    var after = logContent[(idx + "__EXIT_CODE__:".Length)..].Trim();
                    var end = after.IndexOfAny(['\r', '\n']);
                    if (end >= 0) after = after[..end].Trim();
                    if (int.TryParse(after, out var parsed)) realExitCode = parsed;
                }
            }
            catch { }

            return realExitCode;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // User declined UAC
            tailCts.Cancel();
            try { await tailTask; } catch { }
            writeOutput("Workload restore cancelled by user." + Environment.NewLine);
            return -128;
        }
        finally
        {
            tailCts.Cancel();
            try { await tailTask; } catch { }
            try { File.Delete(scriptFile); } catch { }
            try { File.Delete(logFile); } catch { }
        }
    }
}
