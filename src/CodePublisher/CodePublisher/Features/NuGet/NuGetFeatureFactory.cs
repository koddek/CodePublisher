using System.Net;
using System.Net.Http;
using CodePublisher.Features.NuGet.Services;
using CodePublisher.Services;

namespace CodePublisher.Features.NuGet;

public static class NuGetFeatureFactory
{
    public static NuGetViewModel Create(
        PublisherService publisherService,
        DesktopInteractionService desktopInteractionService,
        Action<string> writeLog)
    {
        var httpClient = OperatingSystem.IsBrowser()
            ? new HttpClient { Timeout = TimeSpan.FromMinutes(2) }
            : new HttpClient(new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All
            })
            {
                Timeout = TimeSpan.FromMinutes(2)
            };
        var feedClient = new NuGetFeedClient(httpClient);
        var service = new NuGetPackageService(publisherService.DotnetPath, feedClient);
        return new NuGetViewModel(publisherService, desktopInteractionService, service, writeLog);
    }
}
