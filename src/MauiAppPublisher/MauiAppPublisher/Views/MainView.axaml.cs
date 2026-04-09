using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using DotNetAppPublisher.ViewModels;

namespace DotNetAppPublisher.Views;

public partial class MainView : UserControl
{
    private const double WideBreakpoint = 1200;
    private const double MediumBreakpoint = 800;
    private MainViewModel? _viewModel;

    public MainView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += (_, _) => UpdateResponsiveState(Bounds.Width);
        SizeChanged += (_, e) => UpdateResponsiveState(e.NewSize.Width);
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

    private void UpdateResponsiveState(double width)
    {
        var effectiveWidth = width > 0 ? width : Bounds.Width;
        var isWide = effectiveWidth >= WideBreakpoint;
        var isMedium = effectiveWidth >= MediumBreakpoint && effectiveWidth < WideBreakpoint;
        var isCompact = effectiveWidth < MediumBreakpoint;

        Classes.Set("wide", isWide);
        Classes.Set("medium", isMedium);
        Classes.Set("compact", isCompact);

        UpdateResponsiveLayout(isWide, isMedium);
    }

    private void UpdateResponsiveLayout(bool isWide, bool isMedium)
    {
        if (this.FindControl<StackPanel>("PageStack") is { } pageStack)
        {
            pageStack.Margin = isWide ? new Thickness(18, 18, 18, 24) : new Thickness(12, 12, 12, 18);
            pageStack.Spacing = isWide ? 16 : 12;
        }

        if (this.FindControl<Grid>("HeroGrid") is { } heroGrid)
        {
            heroGrid.ColumnDefinitions = new ColumnDefinitions("*");
            heroGrid.RowDefinitions = new RowDefinitions("Auto,Auto");
        }

        if (this.FindControl<Grid>("ControlGrid") is { } controlGrid)
        {
            controlGrid.ColumnDefinitions = isWide ? new ColumnDefinitions("240,*") : new ColumnDefinitions("*");
            controlGrid.RowDefinitions = isWide ? new RowDefinitions("Auto") : new RowDefinitions("Auto,Auto");
        }

        if (this.FindControl<StackPanel>("DeviceGroup") is { } deviceGroup)
        {
            Grid.SetColumn(deviceGroup, isWide ? 1 : 0);
            Grid.SetRow(deviceGroup, isWide ? 0 : 1);
        }

        if (this.FindControl<Grid>("DeviceActionsGrid") is { } deviceActionsGrid)
        {
            if (isWide)
            {
                deviceActionsGrid.ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto");
                deviceActionsGrid.RowDefinitions = new RowDefinitions("Auto");
            }
            else
            {
                deviceActionsGrid.ColumnDefinitions = new ColumnDefinitions("*");
                deviceActionsGrid.RowDefinitions = new RowDefinitions("Auto,Auto,Auto");
            }
        }

        SetDeviceActionPosition("AndroidDeviceComboBox", 0, 0);
        SetDeviceActionPosition("IosSimulatorComboBox", 0, 0);
        SetDeviceActionPosition("AndroidRefreshButton", isWide ? 1 : 0, isWide ? 0 : 1);
        SetDeviceActionPosition("IosRefreshButton", isWide ? 1 : 0, isWide ? 0 : 1);
        SetDeviceActionPosition("AndroidLaunchButton", isWide ? 2 : 0, isWide ? 0 : 2);
        SetDeviceActionPosition("IosLaunchButton", isWide ? 2 : 0, isWide ? 0 : 2);

        if (this.FindControl<Grid>("MainContentGrid") is { } mainContentGrid)
        {
            if (isWide)
            {
                mainContentGrid.ColumnDefinitions = new ColumnDefinitions("460,*");
                mainContentGrid.RowDefinitions = new RowDefinitions("Auto");
            }
            else
            {
                mainContentGrid.ColumnDefinitions = new ColumnDefinitions("*");
                mainContentGrid.RowDefinitions = new RowDefinitions("Auto,Auto");
            }
        }

        if (this.FindControl<StackPanel>("OutputColumn") is { } outputColumn)
        {
            Grid.SetColumn(outputColumn, isWide ? 1 : 0);
            Grid.SetRow(outputColumn, isWide ? 0 : 1);
        }

        SetScrollHeight("CommandPreviewScrollViewer", isWide ? 180 : isMedium ? 160 : 140);
        SetScrollHeight("KnownGoodScrollViewer", isWide ? 145 : isMedium ? 130 : 120);
        SetScrollHeight("LiveOutputScrollViewer", isWide ? 320 : isMedium ? 260 : 220);
    }

    private void SetScrollHeight(string controlName, double height)
    {
        if (this.FindControl<ScrollViewer>(controlName) is { } scrollViewer)
        {
            scrollViewer.Height = height;
        }
    }

    private void SetDeviceActionPosition(string controlName, int column, int row)
    {
        if (this.FindControl<Control>(controlName) is { } control)
        {
            Grid.SetColumn(control, column);
            Grid.SetRow(control, row);
        }
    }
}
