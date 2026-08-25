using Avalonia.Controls;

namespace DotNetAppPublisher.Services;

public interface IScreenshotService
{
    Task<string> CaptureWindowToClipboardAsync(Window window, CancellationToken cancellationToken);
    Task<string> CaptureWindowToDiskAsync(Window window, string outputDirectory, CancellationToken cancellationToken);
    Task<string> CaptureElementToClipboardAsync(Window window, Control element, CancellationToken cancellationToken);
    Task<string> CaptureElementToDiskAsync(Window window, Control element, string outputDirectory, CancellationToken cancellationToken);
    Task<string> CaptureDetailToClipboardAsync(Window window, ScrollViewer detailScrollViewer, CancellationToken cancellationToken);
    Task<string> CaptureDetailToDiskAsync(Window window, ScrollViewer detailScrollViewer, string outputDirectory, CancellationToken cancellationToken);
}
