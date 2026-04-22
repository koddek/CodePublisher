using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using DotNetAppPublisher.Services;
using DotNetAppPublisher.ViewModels;
using DotNetAppPublisher.Views;

namespace DotNetAppPublisher;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var desktopInteractionService = new DesktopInteractionService();
        var publisherService = new PublisherService();
        var mainViewModel = new MainViewModel(publisherService, desktopInteractionService);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            var mainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };

            desktopInteractionService.Attach(mainWindow);
            desktop.MainWindow = mainWindow;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = mainViewModel
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Data annotations validation is disabled by default in Avalonia 12+
        // See: https://docs.avaloniaui.net/docs/avalonia12-breaking-changes#binding-plugins-removed
    }
}
