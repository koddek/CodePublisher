using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
namespace DotNetAppPublisher.Features.Publishing.Capture;

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

    public async Task<string> CaptureDetailToClipboardAsync(Window window, ScrollViewer detailScrollViewer, CancellationToken cancellationToken)
    {
        try
        {
            var bitmap = await CaptureDetailAsync(window, detailScrollViewer);
            return await CopyBitmapToClipboardAsync(window, bitmap);
        }
        catch (Exception ex)
        {
            return $"Screenshot capture failed: {ex.Message}";
        }
    }

    public async Task<string> CaptureDetailToDiskAsync(Window window, ScrollViewer detailScrollViewer, string outputDirectory, CancellationToken cancellationToken)
    {
        try
        {
            var bitmap = await CaptureDetailAsync(window, detailScrollViewer);
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

    private Task<RenderTargetBitmap> CaptureFullPageAsync(Window window)
    {
        // Complete app window — render the Window's client area as it appears on screen
        // (header + nav + detail viewport + footer). No scroll expansion; that is for detail capture.
        var content = window.Content as Control
            ?? throw new InvalidOperationException("Window content is not a Control.");

        // Render the window itself at its current size to include all chrome (header/nav/footer + visible detail)
        // Fall back to content bounds if window bounds are not yet measured.
        var dpi = window.DesktopScaling;
        var bounds = window.Bounds;
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            bounds = content.Bounds;
        }

        var width = (int)(Math.Max(1, bounds.Width) * dpi);
        var height = (int)(Math.Max(1, bounds.Height) * dpi);

        var bitmap = new RenderTargetBitmap(
            new PixelSize(width, height),
            new Vector(96 * dpi, 96 * dpi));

        // Render the content (MainView) which fills the window — ensures header/nav/footer are included
        content.Measure(bounds.Size);
        content.Arrange(bounds);
        bitmap.Render(content);

        return Task.FromResult(bitmap);
    }

    private async Task<RenderTargetBitmap> CaptureDetailAsync(Window window, ScrollViewer detailScrollViewer)
    {
        var content = detailScrollViewer.Content as Control;
        if (content is null)
        {
            return CaptureControl(window, detailScrollViewer);
        }

        // If detail content is a root StackPanel with multiple section children, find the visible section
        // and capture that instead of the whole root (avoids capturing collapsed sections).
        Control target = content;
        if (content is StackPanel root)
        {
            foreach (var child in root.Children)
            {
                if (child is Control c && c.IsVisible)
                {
                    target = c;
                    break;
                }
            }

            // If target is a section StackPanel, capture it directly with its full desired size.
            if (target != content)
            {
                return await CaptureElementAsync(window, target);
            }
        }

        // Fallback: capture the entire detail content expanded to full height
        var originalMaxHeight = content.MaxHeight;
        var originalHeight = content.Height;
        var originalMinHeight = content.MinHeight;

        try
        {
            content.MaxHeight = double.PositiveInfinity;
            content.MinHeight = 0;
            content.Height = double.NaN;

            detailScrollViewer.MaxHeight = double.PositiveInfinity;
            detailScrollViewer.MinHeight = 0;
            detailScrollViewer.Height = double.NaN;

            content.InvalidateMeasure();
            content.InvalidateArrange();
            detailScrollViewer.InvalidateMeasure();
            await Task.Delay(250);

            var dpi = window.DesktopScaling;
            var bounds = content.Bounds;
            if (bounds.Width <= 1 || bounds.Height <= 1)
            {
                content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                bounds = new Rect(0, 0, content.DesiredSize.Width, content.DesiredSize.Height);
            }

            var width = (int)(Math.Max(1, bounds.Width) * dpi);
            var height = (int)(Math.Max(1, bounds.Height) * dpi);

            var bitmap = new RenderTargetBitmap(
                new PixelSize(Math.Max(1, width), Math.Max(1, height)),
                new Vector(96 * dpi, 96 * dpi));

            bitmap.Render(content);
            return bitmap;
        }
        finally
        {
            content.MaxHeight = originalMaxHeight;
            content.Height = originalHeight;
            content.MinHeight = originalMinHeight;

            detailScrollViewer.ClearValue(ScrollViewer.MaxHeightProperty);
            detailScrollViewer.ClearValue(ScrollViewer.MinHeightProperty);
            detailScrollViewer.ClearValue(ScrollViewer.HeightProperty);

            content.InvalidateMeasure();
            content.InvalidateArrange();
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
