namespace DotNetAppPublisher.Features.Shared.Process;

internal static class ElevatedProcessSecurity
{
    private static readonly string[] SensitiveNameFragments =
    [
        "PASSWORD",
        "TOKEN",
        "SECRET",
        "PRIVATE_KEY",
        "CLIENT_SECRET",
        "API_KEY",
        "ACCESS_KEY",
        "SECRET_KEY",
        "KEYSTORE"
    ];

    internal static void EnsureSafeEnvironment(IReadOnlyDictionary<string, string>? environment)
    {
        if (environment is null)
        {
            return;
        }

        var sensitiveName = environment.Keys.FirstOrDefault(IsSensitiveName);
        if (sensitiveName is not null)
        {
            throw new ArgumentException(
                $"Elevated processes cannot receive sensitive environment variable '{sensitiveName}'.",
                nameof(environment));
        }
    }

    internal static async Task WriteOwnerOnlyAsync(
        string path,
        string contents,
        bool executable,
        CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            await File.WriteAllTextAsync(path, contents, cancellationToken);
            return;
        }

        var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        if (executable)
        {
            mode |= UnixFileMode.UserExecute;
        }

        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous,
            UnixCreateMode = mode
        };

        await using var stream = new FileStream(path, options);
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(contents.AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }

    internal static void RestrictToOwner(string path, bool executable)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        if (executable)
        {
            mode |= UnixFileMode.UserExecute;
        }

        File.SetUnixFileMode(path, mode);
    }

    private static bool IsSensitiveName(string name) =>
        SensitiveNameFragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}
