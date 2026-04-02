using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace MauiAppPublisher.Services;

public sealed class DesktopInteractionService
{
    private Window? _window;

    public Window Window =>
        _window ?? throw new InvalidOperationException("Desktop interaction service is not attached to a window.");

    public void Attach(Window window)
    {
        _window = window;
    }

    public async Task<string?> PickFolderAsync(string title, string? startPath = null)
    {
        var folder = await TryGetFolderAsync(startPath);
        var result = await Window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = folder
        });

        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickFileAsync(string title, string? startPath = null)
    {
        var folder = await TryGetFolderAsync(startPath);
        var result = await Window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = folder,
            FileTypeFilter =
            [
                new FilePickerFileType("Keystore files")
                {
                    Patterns = ["*.jks", "*.keystore"]
                },
                new FilePickerFileType("All files")
                {
                    Patterns = ["*.*"]
                }
            ]
        });

        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public Task ShowInfoAsync(string title, string message)
    {
        return ShowDialogAsync(title, message);
    }

    public Task ShowErrorAsync(string title, string message)
    {
        return ShowDialogAsync(title, message);
    }

    private async Task<IStorageFolder?> TryGetFolderAsync(string? startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath))
        {
            return null;
        }

        if (Directory.Exists(startPath))
        {
            return await Window.StorageProvider.TryGetFolderFromPathAsync(startPath);
        }

        var parent = Path.GetDirectoryName(startPath);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
        {
            return null;
        }

        return await Window.StorageProvider.TryGetFolderFromPathAsync(parent);
    }

    private async Task ShowDialogAsync(string title, string message)
    {
        var button = new Button
        {
            Content = "Close",
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 96
        };

        var dialog = new Window
        {
            Title = title,
            Width = 520,
            MinWidth = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Border
            {
                Padding = new Thickness(20),
                Child = new StackPanel
                {
                    Spacing = 16,
                    Children =
                    {
                        new SelectableTextBlock
                        {
                            Text = message,
                            TextWrapping = TextWrapping.Wrap
                        },
                        button
                    }
                }
            }
        };

        button.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(Window);
    }
}
