using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using DotNetAppPublisher.ViewModels;

namespace DotNetAppPublisher.Views;

public partial class MainView : UserControl
{
    private MainViewModel? _viewModel;

    public MainView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += (_, _) => UnsubscribeViewModel();
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
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(MainViewModel.LiveOutput), StringComparison.Ordinal))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (this.FindControl<ScrollViewer>("LiveOutputScrollViewer") is { } scrollViewer)
            {
                scrollViewer.Offset = new Vector(scrollViewer.Offset.X, scrollViewer.Extent.Height);
            }
        }, DispatcherPriority.Background);
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
}
