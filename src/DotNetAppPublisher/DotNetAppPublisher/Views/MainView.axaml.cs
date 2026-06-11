using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
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
        UpdateThemeIcon();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(MainViewModel.LiveOutput), StringComparison.Ordinal))
        {
            if (this.FindControl<ScrollViewer>("LiveOutputScrollViewer") is { } scrollViewer)
            {
                scrollViewer.Offset = new Vector(scrollViewer.Offset.X, scrollViewer.Extent.Height);
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
