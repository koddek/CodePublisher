using Avalonia.Controls;

namespace DotNetAppPublisher.Services;

public interface IScreenshotService
{
    Task<string> CaptureWindowToClipboardAsync(Window window, CancellationToken cancellationToken);
    Task<string> CaptureWindowToDiskAsync(Window window, string outputDirectory, CancellationToken cancellationToken);
}
