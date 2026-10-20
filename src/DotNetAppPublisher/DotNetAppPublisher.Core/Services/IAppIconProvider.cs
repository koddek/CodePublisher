namespace DotNetAppPublisher.Services;

/// <summary>
/// Supplies the macOS application icon used when a publish produces a <c>.app</c> bundle.
/// The bytes live in the host application's resources, so the domain layer asks for them through
/// this seam instead of reading a resource loader directly.
/// </summary>
public interface IAppIconProvider
{
    /// <summary>The icon bytes, or null when the host has no icon to contribute.</summary>
    byte[]? GetIconBytes();
}
