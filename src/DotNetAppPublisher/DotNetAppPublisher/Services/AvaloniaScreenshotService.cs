using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
namespace DotNetAppPublisher.Services;

public class AvaloniaScreenshotService : IScreenshotService
{
    public async Task<string> CaptureWindowToClipboardAsync(Window window, CancellationToken cancellationToken)
    {
        try
        {
            var bitmap = await CaptureFullPageAsync(window);
            return await CopyBitmapToClipboardAsync(window, bitmap);
        }
        catch (Exception ex)
        {
            return $"Screenshot capture failed: {ex.Message}";
        }
    }

    public async Task<string> CaptureWindowToDiskAsync(Window window, string outputDirectory, CancellationToken cancellationToken)
    {
        try
        {
            var bitmap = await CaptureFullPageAsync(window);
            return await SaveBitmapToDiskAsync(bitmap, outputDirectory, "window");
        }
        catch (Exception ex)
        {
            return $"Screenshot capture failed: {ex.Message}";
        }
    }

    public async Task<string> CaptureElementToClipboardAsync(Window window, Control element, CancellationToken cancellationToken)
    {
        try
        {
            var bitmap = await CaptureElementAsync(window, element);
            return await CopyBitmapToClipboardAsync(window, bitmap);
        }
        catch (Exception ex)
        {
            return $"Screenshot capture failed: {ex.Message}";
        }
    }

    public async Task<string> CaptureElementToDiskAsync(Window window, Control element, string outputDirectory, CancellationToken cancellationToken)
    {
        try
        {
            var bitmap = await CaptureElementAsync(window, element);
            return await SaveBitmapToDiskAsync(bitmap, outputDirectory, "detail");
        }
        catch (Exception ex)
        {
            return $"Screenshot capture failed: {ex.Message}";
        }
    }

    private static async Task<string> CopyBitmapToClipboardAsync(Window window, Bitmap bitmap)
    {
        var clipboard = window.Clipboard;
        if (clipboard is null)
        {
            return "Clipboard not available.";
        }

        try
        {
            await ClipboardExtensions.SetBitmapAsync(clipboard, bitmap);
            return "Screenshot copied to clipboard as image.";
        }
        catch
        {
            using var stream = new MemoryStream();
            bitmap.Save(stream, new PngBitmapEncoderOptions());
            var pngBytes = stream.ToArray();
            var fallbackPath = Path.Combine(Path.GetTempPath(), $"DotNetAppPublisher-screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            await File.WriteAllBytesAsync(fallbackPath, pngBytes);
            await ClipboardExtensions.SetTextAsync(clipboard, fallbackPath);
            return $"Clipboard image unavailable; saved to {fallbackPath} and path copied.";
        }
    }

    private static async Task<string> SaveBitmapToDiskAsync(Bitmap bitmap, string outputDirectory, string suffix)
    {
        var targetDirectory = ResolveScreenshotDirectory(outputDirectory);
        Directory.CreateDirectory(targetDirectory);

        var filePath = Path.Combine(
            targetDirectory,
            $"DotNetAppPublisher-{suffix}-{DateTime.Now:yyyyMMdd-HHmmss}.png");

        using (var fileStream = File.Create(filePath))
        {
            bitmap.Save(fileStream, new PngBitmapEncoderOptions());
        }

        await Task.CompletedTask;
        return $"Screenshot saved to {filePath}.";
    }

    private async Task<RenderTargetBitmap> CaptureFullPageAsync(Window window)
    {
        var content = window.Content as Control
            ?? throw new InvalidOperationException("Window content is not a Control.");

        var scrollViewer = FindScrollViewer(content);
        if (scrollViewer is null)
        {
            return CaptureControl(window, content);
        }

        var contentControl = scrollViewer.Content as Control;
        if (contentControl is null)
        {
            return CaptureControl(window, scrollViewer);
        }

        var extentHeight = scrollViewer.Extent.Height;
        var extentWidth = scrollViewer.Extent.Width;

        if (extentHeight <= 0 || extentWidth <= 0)
        {
            return CaptureControl(window, content);
        }

        var originalMaxHeight = contentControl.MaxHeight;
        var originalMaxWidth = contentControl.MaxWidth;
        var originalHeight = contentControl.Height;
        var originalWidth = contentControl.Width;
        var originalMinHeight = contentControl.MinHeight;
        var originalMinWidth = contentControl.MinWidth;

        try
        {
            contentControl.MaxHeight = double.PositiveInfinity;
            contentControl.MaxWidth = double.PositiveInfinity;
            contentControl.MinHeight = 0;
            contentControl.MinWidth = 0;
            contentControl.Height = double.NaN;
            contentControl.Width = double.NaN;

            scrollViewer.MaxHeight = double.PositiveInfinity;
            scrollViewer.MaxWidth = double.PositiveInfinity;
            scrollViewer.MinHeight = 0;
            scrollViewer.MinWidth = 0;
            scrollViewer.Height = double.NaN;
            scrollViewer.Width = double.NaN;

            contentControl.InvalidateMeasure();
            contentControl.InvalidateArrange();
            contentControl.InvalidateVisual();

            await Task.Delay(300);

            var dpi = window.DesktopScaling;
            var bounds = contentControl.Bounds;
            var width = (int)(bounds.Width * dpi);
            var height = (int)(bounds.Height * dpi);

            var bitmap = new RenderTargetBitmap(
                new PixelSize(Math.Max(1, width), Math.Max(1, height)),
                new Vector(96 * dpi, 96 * dpi));

            bitmap.Render(contentControl);

            return bitmap;
        }
        finally
        {
            contentControl.MaxHeight = originalMaxHeight;
            contentControl.MaxWidth = originalMaxWidth;
            contentControl.MinHeight = originalMinHeight;
            contentControl.MinWidth = originalMinWidth;
            contentControl.Height = originalHeight;
            contentControl.Width = originalWidth;

            // Restore scroll viewer to natural constraints so scroll works after capture
            scrollViewer.ClearValue(ScrollViewer.MaxHeightProperty);
            scrollViewer.ClearValue(ScrollViewer.MaxWidthProperty);
            scrollViewer.ClearValue(ScrollViewer.MinHeightProperty);
            scrollViewer.ClearValue(ScrollViewer.MinWidthProperty);
            scrollViewer.ClearValue(ScrollViewer.HeightProperty);
            scrollViewer.ClearValue(ScrollViewer.WidthProperty);

            contentControl.InvalidateMeasure();
            contentControl.InvalidateArrange();
            contentControl.InvalidateVisual();
        }
    }

    private async Task<RenderTargetBitmap> CaptureElementAsync(Window window, Control element)
    {
        // Ensure element is measured at its desired full height (unclamped) for a clean capture.
        var originalMaxHeight = element.MaxHeight;
        var originalMaxWidth = element.MaxWidth;
        var originalHeight = element.Height;
        var originalWidth = element.Width;

        try
        {
            // Let element grow to its desired size for capture
            element.MaxHeight = double.PositiveInfinity;
            element.MaxWidth = double.PositiveInfinity;
            element.InvalidateMeasure();
            element.InvalidateArrange();
            await Task.Delay(150);

            var dpi = window.DesktopScaling;
            var bounds = element.Bounds;
            // Fallback to desired size if bounds still zero
            if (bounds.Width <= 1 || bounds.Height <= 1)
            {
                element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                bounds = new Rect(0, 0, element.DesiredSize.Width, element.DesiredSize.Height);
            }

            var width = (int)(Math.Max(1, bounds.Width) * dpi);
            var height = (int)(Math.Max(1, bounds.Height) * dpi);

            var bitmap = new RenderTargetBitmap(
                new PixelSize(Math.Max(1, width), Math.Max(1, height)),
                new Vector(96 * dpi, 96 * dpi));

            bitmap.Render(element);
            return bitmap;
        }
        finally
        {
            element.MaxHeight = originalMaxHeight;
            element.MaxWidth = originalMaxWidth;
            element.Height = originalHeight;
            element.Width = originalWidth;
            element.InvalidateMeasure();
            element.InvalidateArrange();
        }
    }

    private static RenderTargetBitmap CaptureControl(Window window, Control control)
    {
        var dpi = window.DesktopScaling;
        var bounds = control.Bounds;
        var width = (int)(bounds.Width * dpi);
        var height = (int)(bounds.Height * dpi);

        var bitmap = new RenderTargetBitmap(
            new PixelSize(Math.Max(1, width), Math.Max(1, height)),
            new Vector(96 * dpi, 96 * dpi));

        bitmap.Render(control);

        return bitmap;
    }

    private static ScrollViewer? FindScrollViewer(Control control)
    {
        if (control is ScrollViewer sv)
            return sv;

        foreach (var child in control.GetVisualChildren())
        {
            if (child is Control childControl)
            {
                var result = FindScrollViewer(childControl);
                if (result is not null)
                    return result;
            }
        }

        return null;
    }

    private static string ResolveScreenshotDirectory(string outputDirectory)
    {
        if (!string.IsNullOrWhiteSpace(outputDirectory) && Directory.Exists(outputDirectory))
        {
            return outputDirectory;
        }

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!string.IsNullOrWhiteSpace(desktop))
        {
            return desktop;
        }

        return Path.GetTempPath();
    }
}
