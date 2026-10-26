using System.Globalization;

namespace CodePublisher.Features.Deployment.Android;

/// <summary>
/// Parses `ls -la` output from an Android device (toybox ls, Android 7+). Pure so every shape the
/// device can produce — folders, files, symlinks, names with spaces, error lines — is unit tested.
/// </summary>
public static class AndroidDeviceFileListParser
{
    private static readonly string[] TimestampFormats =
    [
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd HH:mm:ss"
    ];

    public static IReadOnlyList<AndroidDeviceFileEntry> Parse(string? lsOutput, string directoryPath)
    {
        var directory = AndroidStorageLocations.NormalizeRemotePath(directoryPath);
        var entries = new List<AndroidDeviceFileEntry>();
        if (string.IsNullOrWhiteSpace(lsOutput))
        {
            return entries;
        }

        foreach (var rawLine in lsOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.TrimEnd();
            if (line.Length == 0
                || line.StartsWith("total ", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("ls:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // perms links owner group size date time name...
            var fields = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 8 || fields[0].Length < 10)
            {
                continue;
            }

            var permissions = fields[0];
            var isDirectory = permissions[0] == 'd';
            var isSymlink = permissions[0] == 'l';

            var name = string.Join(' ', fields.Skip(7));
            if (isSymlink)
            {
                // `ls -l` appends the link target; browsing always follows the entry itself.
                var arrow = name.IndexOf(" -> ", StringComparison.Ordinal);
                if (arrow > 0)
                {
                    name = name[..arrow];
                }
            }

            if (name.Length == 0 || name == "." || name == "..")
            {
                continue;
            }

            _ = long.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var size);
            var modified = ParseTimestamp(fields[5], fields[6]);

            entries.Add(new AndroidDeviceFileEntry(
                name,
                AndroidStorageLocations.CombineRemotePath(directory, name),
                isDirectory,
                isDirectory ? 0 : size,
                modified));
        }

        return entries
            .OrderByDescending(entry => entry.IsDirectory)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static DateTimeOffset? ParseTimestamp(string date, string time)
    {
        foreach (var format in TimestampFormats)
        {
            if (DateTimeOffset.TryParseExact(
                    $"{date} {time}",
                    format,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal,
                    out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }
}
