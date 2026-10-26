using Avalonia.Platform;

namespace CodePublisher.Services;

/// <summary>
/// Reads the application icon from the Avalonia resource system so the domain layer can stamp it
/// into a generated macOS <c>.app</c> bundle without knowing how resources are loaded.
/// </summary>
public sealed class AvaloniaAppIconProvider : IAppIconProvider
{
    private const string IconAssetPath = "avares://CodePublisher/Assets/codepublisher.icns";

    public byte[]? GetIconBytes()
    {
        try
        {
            using var source = AssetLoader.Open(new Uri(IconAssetPath));
            using var buffer = new MemoryStream();
            source.CopyTo(buffer);
            return buffer.ToArray();
        }
        catch (Exception)
        {
            // A missing icon must never fail a publish; the bundle is still usable without it.
            return null;
        }
    }
}
