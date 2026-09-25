using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace DotNetAppPublisher.Services;

public sealed class DesktopInteractionService
{
    private enum AlertKind
    {
        Info,
        Error
    }

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

    public async Task<string?> PickFileAsync(string title, string? startPath = null, IEnumerable<FilePickerFileType>? fileTypeFilter = null)
    {
        var folder = await TryGetFolderAsync(startPath);
        var filter = fileTypeFilter?.ToList() ?? [new FilePickerFileType("All files") { Patterns = ["*.*"] }];
        var result = await Window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = folder,
            FileTypeFilter = filter
        });

        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public Task ShowInfoAsync(string title, string message)
    {
        return ShowDialogAsync(title, message, AlertKind.Info);
    }

    public Task ShowErrorAsync(string title, string message)
    {
        return ShowDialogAsync(title, message, AlertKind.Error);
    }

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        var confirmButton = new Button
        {
            Content = "Continue",
            MinWidth = 96
        };
        confirmButton.Classes.Add("primary");

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 96
        };

        var actions = new WrapPanel
        {
            ItemSpacing = 8,
            LineSpacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        actions.Children.Add(cancelButton);
        actions.Children.Add(confirmButton);

        var dialog = CreateDialogWindow(title, CreateDialogContent(title, message, actions, AlertKind.Info));
        var confirmed = false;
        confirmButton.Click += (_, _) =>
        {
            confirmed = true;
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        await dialog.ShowDialog(Window);
        return confirmed;
    }

    public async Task CopyTextToClipboardAsync(string text)
    {
        await ClipboardExtensions.SetTextAsync(Window.Clipboard!, text);
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

    private async Task ShowDialogAsync(string title, string message, AlertKind kind)
    {
        var closeButton = new Button
        {
            Content = "Close",
            MinWidth = 96,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        closeButton.Classes.Add("primary");

        var dialog = CreateDialogWindow(title, CreateDialogContent(title, message, closeButton, kind));
        closeButton.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(Window);
    }

    private Window CreateDialogWindow(string title, Control content)
    {
        var ownerWidth = Window.Bounds.Width;
        var dialogWidth = double.IsFinite(ownerWidth)
            ? Math.Clamp(ownerWidth - 32, 320, 520)
            : 520;

        return new Window
        {
            Title = title,
            Width = dialogWidth,
            MinWidth = 320,
            MaxWidth = 680,
            SizeToContent = SizeToContent.Height,
            CanResize = true,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = content
        };
    }

    private static Control CreateDialogContent(string title, string message, Control actions, AlertKind kind)
    {
        var header = new TextBlock
        {
            Text = title,
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        };
        header.Classes.Add(kind == AlertKind.Error ? "alert-error" : "alert-info");

        var details = new SelectableTextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 12)
        };
        var detailsHost = new Border
        {
            MinHeight = 96,
            MaxHeight = 260,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = new Border
                {
                    MinHeight = 96,
                    Child = details
                }
            }
        };

        var content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            ColumnDefinitions = new ColumnDefinitions("*")
        };
        Grid.SetRow(header, 0);
        Grid.SetRow(detailsHost, 1);
        Grid.SetRow(actions, 2);
        content.Children.Add(header);
        content.Children.Add(detailsHost);
        content.Children.Add(actions);

        return new Border
        {
            Padding = new Thickness(24),
            Child = content
        };
    }
}
