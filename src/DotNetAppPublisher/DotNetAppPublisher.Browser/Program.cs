using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using Avalonia.Rendering;
using DotNetAppPublisher;

internal sealed partial class Program
{
    private static Task Main(string[] args) => BuildAvaloniaApp()
        .WithInterFont()
        .StartBrowserAppAsync("out", new BrowserPlatformOptions
        {
            RenderingMode = [BrowserRenderingMode.Software2D],
            RegisterAvaloniaServiceWorker = false
        });

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}