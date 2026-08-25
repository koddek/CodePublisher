using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using DotNetAppPublisher.Services;
using DotNetAppPublisher.ViewModels;

namespace DotNetAppPublisher.Views;

public partial class MainView : UserControl
{
    private MainViewModel? _viewModel;

    public MainView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        UnsubscribeViewModel();
        _viewModel = DataContext as MainViewModel;
        if (_viewModel is null)
        {
            return;
        }
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateThemeIcon();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(MainViewModel.LiveOutput), StringComparison.Ordinal))
        {
            if (this.FindControl<ScrollViewer>("DetailScrollViewer") is { } sv)
            {
                // keep scrolled near bottom when log grows and Log section is visible
                if (_viewModel?.IsLogSection == true)
                {
                    sv.Offset = new Vector(sv.Offset.X, sv.Extent.Height);
                }
            }
        }
        else if (string.Equals(e.PropertyName, nameof(MainViewModel.Theme), StringComparison.Ordinal))
        {
            UpdateThemeIcon();
        }
    }

    private void UpdateThemeIcon()
    {
        if (this.FindControl<PathIcon>("ThemeIcon") is not { } icon)
        {
            return;
        }

        var pathData = _viewModel?.Theme switch
        {
            "Light" => "M12 7c-2.76 0-5 2.24-5 5s2.24 5 5 5 5-2.24 5-5-2.24-5-5-5zM2 13h2c.55 0 1-.45 1-1s-.45-1-1-1H2c-.55 0-1 .45-1 1s.45 1 1 1zm18 0h2c.55 0 1-.45 1-1s-.45-1-1-1h-2c-.55 0-1 .45-1 1s.45 1 1 1zM11 2v2c0 .55.45 1 1 1s1-.45 1-1V2c0-.55-.45-1-1-1s-1 .45-1 1zm0 18v2c0 .55.45 1 1 1s1-.45 1-1v-2c0-.55-.45-1-1-1s-1 .45-1 1zM5.99 4.58c-.39-.39-1.03-.39-1.41 0-.39.39-.39 1.03 0 1.41l1.06 1.06c.39.39 1.03.39 1.41 0s.39-1.03 0-1.41L5.99 4.58zm12.37 12.37c-.39-.39-1.03-.39-1.41 0-.39.39-.39 1.03 0 1.41l1.06 1.06c.39.39 1.03.39 1.41 0 .39-.39.39-1.03 0-1.41l-1.06-1.06zm1.06-10.96c.39-.39.39-1.03 0-1.41-.39-.39-1.03-.39-1.41 0l-1.06 1.06c-.39.39-.39 1.03 0 1.41s1.03.39 1.41 0l1.06-1.06zM7.05 18.36c.39-.39.39-1.03 0-1.41-.39-.39-1.03-.39-1.41 0l-1.06 1.06c-.39.39-.39 1.03 0 1.41s1.03.39 1.41 0l1.06-1.06z",
            "Dark" => "M12 3c-4.97 0-9 4.03-9 9s4.03 9 9 9 9-4.03 9-9c0-.46-.04-.92-.1-1.36-.98 1.37-2.58 2.26-4.4 2.26-3.03 0-5.5-2.47-5.5-5.5 0-1.82.89-3.42 2.26-4.4-.44-.06-.9-.1-1.36-.1z",
            _ => "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8zm-1-7h2v2h-2zm0 4h2v2h-2z"
        };

        icon.Data = Geometry.Parse(pathData);
    }

    private void UnsubscribeViewModel()
    {
        if (_viewModel is null)
        {
            return;
        }
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = null;
    }

    private async void OnCaptureDetailClick(object? sender, RoutedEventArgs e)
    {
        var detailHost = this.FindControl<ScrollViewer>("DetailScrollViewer");
        if (detailHost is null)
            return;

        var window = TopLevel.GetTopLevel(this) as Window;
        if (window is null || _viewModel is null)
            return;

        var dialog = new ScreenshotDialog();
        await dialog.ShowCountdownAndCaptureAsync(5, async () =>
        {
            var svc = new AvaloniaScreenshotService();
            return await svc.CaptureDetailToDiskAsync(window, detailHost, _viewModel.OutputDirectory, CancellationToken.None);
        });
    }
}
