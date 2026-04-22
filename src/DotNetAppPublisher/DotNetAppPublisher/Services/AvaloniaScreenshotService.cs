using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
            
            var clipboard = window.Clipboard;
            if (clipboard is null)
            {
                return "Clipboard not available.";
            }

            using var stream = new MemoryStream();
            bitmap.Save(stream);
            stream.Position = 0;
            
            var base64 = Convert.ToBase64String(stream.ToArray());
            await ClipboardExtensions.SetTextAsync(clipboard, base64);
            
            return "Full-page screenshot copied to clipboard.";
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
            
            var targetDirectory = ResolveScreenshotDirectory(outputDirectory);
            Directory.CreateDirectory(targetDirectory);
            
            var filePath = Path.Combine(
                targetDirectory,
                $"DotNetAppPublisher-screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            
            using (var fileStream = File.Create(filePath))
            {
                bitmap.Save(fileStream);
            }
            
            return $"Full-page screenshot saved to {filePath}.";
        }
        catch (Exception ex)
        {
            return $"Screenshot capture failed: {ex.Message}";
        }
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
            
            scrollViewer.MaxHeight = double.PositiveInfinity;
            scrollViewer.MaxWidth = double.PositiveInfinity;
            scrollViewer.MinHeight = 0;
            scrollViewer.MinWidth = 0;
            
            contentControl.InvalidateMeasure();
            contentControl.InvalidateArrange();
            contentControl.InvalidateVisual();
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
