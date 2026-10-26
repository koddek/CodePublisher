namespace CodePublisher.Features.Deployment.Android;

/// <summary>One folder or file inside the device file browser.</summary>
public sealed record AndroidDeviceFileEntry(
    string Name,
    string FullPath,
    bool IsDirectory,
    long SizeBytes,
    DateTimeOffset? Modified)
{
    public const string FolderIconData =
        "M10 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z";

    public const string FileIconData =
        "M6 2c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6H6zm7 7V3.5L18.5 9H13z";

    public bool IsFile => !IsDirectory;

    public string IconData => IsDirectory ? FolderIconData : FileIconData;

    /// <summary>Folders report no size in `ls`, so the size column stays empty for them.</summary>
    public string SizeText => IsDirectory ? string.Empty : FormatSize(SizeBytes);

    public string ModifiedText => Modified is { } modified
        ? modified.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
        : string.Empty;

    public string DetailText
    {
        get
        {
            var parts = new[] { SizeText, ModifiedText }.Where(text => !string.IsNullOrEmpty(text)).ToArray();
            return parts.Length == 0 ? (IsDirectory ? "Folder" : string.Empty) : string.Join(" · ", parts);
        }
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        < 0 => string.Empty,
        < 1024 => $"{bytes} B",
        < 1024L * 1024 => $"{bytes / 1024d:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024):0.#} MB",
        _ => $"{bytes / (1024d * 1024 * 1024):0.##} GB"
    };
}
