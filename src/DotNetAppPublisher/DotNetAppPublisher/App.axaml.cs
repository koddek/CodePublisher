using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Avalonia.Markup.Xaml;
using DotNetAppPublisher.Features.Deployment.Android;
using DotNetAppPublisher.Features.GooglePlay;
using DotNetAppPublisher.Services;
using DotNetAppPublisher.ViewModels;
using DotNetAppPublisher.Views;

namespace DotNetAppPublisher;

public partial class App : Application
{
    private static readonly string DiagLogPath = Path.Combine(Path.GetTempPath(), "dotnet-app-publisher-trim-diag.log");

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties |
        DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods,
        typeof(MainViewModel))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties |
        DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods,
        typeof(Features.GooglePlay.GooglePlayViewModel))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(AndroidDeviceInfo))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(NavItem))]
    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            var desktopInteractionService = new DesktopInteractionService();
            var publisherService = new PublisherService();
            var googlePlayViewModel = GooglePlayFeatureFactory.Create(publisherService, desktopInteractionService);
            var mainViewModel = new MainViewModel(publisherService, desktopInteractionService, googlePlayViewModel);

            // File-system diagnostics are not supported by the browser runtime.
            if (!OperatingSystem.IsBrowser())
            {
                var diag = new System.Text.StringBuilder();
                diag.AppendLine("[TRIM-DIAG] MainViewModel created.");
                diag.AppendLine($"[TRIM-DIAG] IsEditorEnabled={mainViewModel.IsEditorEnabled}");
                diag.AppendLine($"[TRIM-DIAG] BrowseProjectDirectoryCommand type={mainViewModel.BrowseProjectDirectoryCommand?.GetType().FullName ?? "NULL"}");
                diag.AppendLine($"[TRIM-DIAG] BrowseProjectDirectoryCommand.CanExecute={mainViewModel.BrowseProjectDirectoryCommand?.CanExecute(null)}");
                diag.AppendLine($"[TRIM-DIAG] PublishCommand type={mainViewModel.PublishCommand?.GetType().FullName ?? "NULL"}");
                diag.AppendLine($"[TRIM-DIAG] PublishCommand.CanExecute={mainViewModel.PublishCommand?.CanExecute(null)}");
                diag.AppendLine($"[TRIM-DIAG] RefreshDevicesCommand type={mainViewModel.RefreshDevicesCommand?.GetType().FullName ?? "NULL"}");
                diag.AppendLine($"[TRIM-DIAG] PublishPlatformOptions.Count={mainViewModel.PublishPlatformOptions.Count}");
                foreach (var opt in mainViewModel.PublishPlatformOptions)
                    diag.AppendLine($"[TRIM-DIAG]   Platform: '{opt}'");
                diag.AppendLine($"[TRIM-DIAG] ConfigurationOptions.Count={mainViewModel.ConfigurationOptions.Count}");
                foreach (var opt in mainViewModel.ConfigurationOptions)
                    diag.AppendLine($"[TRIM-DIAG]   Config: '{opt}'");
                diag.AppendLine($"[TRIM-DIAG] LinkModeOptions.Count={mainViewModel.LinkModeOptions.Count}");
                diag.AppendLine($"[TRIM-DIAG] SignModeOptions.Count={mainViewModel.SignModeOptions.Count}");
                diag.AppendLine($"[TRIM-DIAG] ThemeOptions.Count={mainViewModel.ThemeOptions.Count}");
                diag.AppendLine($"[TRIM-DIAG] IsBusy={mainViewModel.IsBusy}");
                diag.AppendLine($"[TRIM-DIAG] PublishPlatform={mainViewModel.PublishPlatform}");
                diag.AppendLine($"[TRIM-DIAG] IsAndroidPlatform={mainViewModel.IsAndroidPlatform}");
                diag.AppendLine($"[TRIM-DIAG] AvailableAndroidDevices.Count={mainViewModel.AvailableAndroidDevices.Count}");
                File.WriteAllText(DiagLogPath, diag.ToString());
            }

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
                mainWindow.Closing += (_, _) => googlePlayViewModel.ClearSessionAndForget();
                desktop.MainWindow = mainWindow;
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
            {
                singleViewPlatform.MainView = new MainView
                {
                    DataContext = mainViewModel
                };
            }
        }
        catch (Exception ex)
        {
            if (!OperatingSystem.IsBrowser())
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "dotnet-app-publisher-trim-crash.log"),
                    $"CRITICAL FAILURE in OnFrameworkInitializationCompleted:\n{ex}");
            }

            throw;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Data annotations validation is disabled by default in Avalonia 12+
        // See: https://docs.avaloniaui.net/docs/avalonia12-breaking-changes#binding-plugins-removed
    }
}
