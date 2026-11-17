namespace CodePublisher.Features.GooglePlay.Images;

/// <summary>
/// One store-listing image selected for upload with a publish. <see cref="ImageType"/> is the exact
/// value the Play API expects, and <see cref="Language"/> is a BCP-47 tag.
/// </summary>
public sealed record GooglePlayStoreImage(string ImageType, string Language, string Path)
{
    public string FileName => System.IO.Path.GetFileName(Path);
}

/// <summary>A supported Play store-listing image slot.</summary>
/// <param name="ImageType">The Play API imageType value.</param>
/// <param name="Label">The label shown in the app.</param>
/// <param name="Hint">The requirement shown next to the picker.</param>
/// <param name="MaxBytes">The largest file Play accepts for this slot.</param>
public sealed record GooglePlayImageType(string ImageType, string Label, string Hint, long MaxBytes);

/// <summary>
/// The image slots the Play API can update in an edit, and the checks we can do locally before an
/// upload. Pure policy: no I/O beyond the file metadata the caller already has.
/// </summary>
public static class GooglePlayStoreImageCatalog
{
    public const string Phone = "phoneScreenshots";
    public const string TabletSevenInch = "sevenInchScreenshots";
    public const string TabletTenInch = "tenInchScreenshots";
    public const string FeatureGraphic = "featureGraphic";
    public const string Icon = "icon";
    public const string TvScreenshots = "tvScreenshots";
    public const string TvBanner = "tvBanner";
    public const string WearScreenshots = "wearScreenshots";

    private const long FifteenMegabytes = 15L * 1024 * 1024;
    private const long OneMegabyte = 1024L * 1024;

    public static IReadOnlyList<GooglePlayImageType> Types { get; } =
    [
        new(Phone, "Phone screenshots", "JPEG or PNG, 320-3840 px, up to 8 images.", FifteenMegabytes),
        new(TabletSevenInch, "7-inch tablet screenshots", "JPEG or PNG, 320-3840 px, up to 8 images.", FifteenMegabytes),
        new(TabletTenInch, "10-inch tablet screenshots", "JPEG or PNG, 320-3840 px, up to 8 images.", FifteenMegabytes),
        new(FeatureGraphic, "Feature graphic", "JPEG or 24-bit PNG, 1024 x 500 px.", FifteenMegabytes),
        new(Icon, "Store icon", "32-bit PNG with alpha, 512 x 512 px, up to 1 MB.", OneMegabyte),
        new(TvScreenshots, "TV screenshots", "JPEG or PNG. TV apps need at least one.", FifteenMegabytes),
        new(TvBanner, "TV banner", "JPEG or PNG. TV apps need one with TV screenshots.", FifteenMegabytes),
        new(WearScreenshots, "Wear OS screenshots", "JPEG or PNG, square, at least 384 x 384 px.", FifteenMegabytes),
    ];

    public static bool IsKnownType(string imageType) =>
        Types.Any(type => string.Equals(type.ImageType, imageType, StringComparison.Ordinal));

    public static GooglePlayImageType? Describe(string imageType) =>
        Types.FirstOrDefault(type => string.Equals(type.ImageType, imageType, StringComparison.Ordinal));

    /// <summary>True when the extension is one Play accepts for a store-listing image.</summary>
    public static bool IsSupportedExtension(string path)
    {
        var extension = System.IO.Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    public static string MediaType(string path) =>
        System.IO.Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase)
            ? "image/png"
            : "image/jpeg";

    /// <summary>
    /// Returns an error message, or null when the file can be uploaded. Checks only what is certain
    /// locally: the file exists, is not empty, has a supported extension and fits the slot's limit.
    /// Pixel dimensions are left to Play, which validates them against the listing requirements.
    /// </summary>
    public static string? Validate(GooglePlayStoreImage image)
    {
        if (string.IsNullOrWhiteSpace(image.Path))
        {
            return "Choose a file for this image.";
        }

        if (string.IsNullOrWhiteSpace(image.Language))
        {
            return "Enter a language, for example en-US.";
        }

        if (!IsKnownType(image.ImageType))
        {
            return $"Unknown image type `{image.ImageType}`.";
        }

        if (!IsSupportedExtension(image.Path))
        {
            return "Store images must be PNG or JPEG files.";
        }

        if (string.Equals(image.ImageType, Icon, StringComparison.Ordinal) &&
            !System.IO.Path.GetExtension(image.Path).Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            return "The store icon must be a PNG with alpha, 512 x 512 px.";
        }

        FileInfo file;
        try
        {
            file = new FileInfo(image.Path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or System.IO.PathTooLongException)
        {
            return "That image path is not valid.";
        }

        if (!file.Exists)
        {
            return $"Image file not found: {image.Path}";
        }

        if (file.Length == 0)
        {
            return "The image file is empty.";
        }

        var limit = Describe(image.ImageType)!.MaxBytes;
        return file.Length > limit
            ? $"The image is larger than the {limit / (1024 * 1024)} MB Play accepts for this slot."
            : null;
    }
}
