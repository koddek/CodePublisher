using System.Net;
using System.Net.Http;
using System.Text;
using CodePublisher.Features.GooglePlay.Authentication;
using CodePublisher.Features.GooglePlay.Images;
using CodePublisher.Features.GooglePlay.Models;
using CodePublisher.Features.GooglePlay.Services;
using CodePublisher.Features.GooglePlay.State;

namespace CodePublisher.Tests.Features.GooglePlay;

public sealed class GooglePlayStoreImageTests
{
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Uri, string? ContentType)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add((
                request.Method,
                request.RequestUri!.ToString(),
                request.Content?.Headers.ContentType?.MediaType));

            var response = new HttpResponseMessage(HttpStatusCode.OK);
            if (request.RequestUri.AbsolutePath.Contains("/bundles") && request.Method == HttpMethod.Post)
            {
                response.Headers.Location = new Uri("https://androidpublisher.googleapis.com/session");
                response.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            }
            else if (request.RequestUri.AbsolutePath.Contains("/bundles") ||
                     request.RequestUri.AbsolutePath == "/session")
            {
                response.Content = new StringContent("""{"versionCode":42}""", Encoding.UTF8, "application/json");
            }
            else if (request.RequestUri.AbsolutePath.EndsWith("/edits", StringComparison.Ordinal))
            {
                response.Content = new StringContent("""{"id":"edit-1"}""", Encoding.UTF8, "application/json");
            }
            else
            {
                response.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            }

            return Task.FromResult(response);
        }
    }

    private sealed class FakeCredentialStore : IGoogleCredentialStore
    {
        public GooglePlayAccountInfo Add(GoogleOAuthTokens tokens, GooglePlayAccountInfo account) => account;
        public Task<string?> GetAccessTokenAsync(string accountId, CancellationToken cancellationToken) => Task.FromResult<string?>("token");
        public Task RevokeAsync(string accountId, CancellationToken cancellationToken) => Task.CompletedTask;
        public void Forget(string accountId)
        {
        }
    }

    private static string WritePng()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cp-image-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        return path;
    }

    [Test]
    public async Task Catalog_ValidatesFilesWeCanCheckLocally()
    {
        var path = WritePng();
        try
        {
            using (Assert.Multiple())
            {
                await Assert.That(GooglePlayStoreImageCatalog.Validate(new GooglePlayStoreImage(
                    GooglePlayStoreImageCatalog.Phone, "en-US", path))).IsNull();
                await Assert.That(GooglePlayStoreImageCatalog.Validate(new GooglePlayStoreImage(
                    GooglePlayStoreImageCatalog.Phone, " ", path))).IsEqualTo("Enter a language, for example en-US.");
                await Assert.That(GooglePlayStoreImageCatalog.Validate(new GooglePlayStoreImage(
                    "unknown", "en-US", path))).IsEqualTo("Unknown image type `unknown`.");
                await Assert.That(GooglePlayStoreImageCatalog.IsSupportedExtension("shot.jpg")).IsTrue();
                await Assert.That(GooglePlayStoreImageCatalog.IsSupportedExtension("shot.webp")).IsFalse();
                await Assert.That(GooglePlayStoreImageCatalog.MediaType(path)).IsEqualTo("image/png");
                await Assert.That(GooglePlayStoreImageCatalog.Validate(new GooglePlayStoreImage(
                    GooglePlayStoreImageCatalog.Icon, "en-US", Path.ChangeExtension(path, ".jpg"))))
                    .IsEqualTo("The store icon must be a PNG with alpha, 512 x 512 px.");
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task UploadUri_UsesTheMediaEndpointWithTheExactImageType()
    {
        var uri = GooglePlayApiService.BuildStoreImageUploadUri(
            "com.example.app", "edit-1", "de-AT", GooglePlayStoreImageCatalog.TabletTenInch);

        await Assert.That(uri).IsEqualTo(
            "https://androidpublisher.googleapis.com/upload/androidpublisher/v3/applications/" +
            "com.example.app/edits/edit-1/listings/de-AT/tenInchScreenshots?uploadType=media");
    }

    [Test]
    public async Task StateHolder_StoresAndClearsImages()
    {
        var holder = new GooglePlayStateHolder();
        var image = new GooglePlayStoreImage(GooglePlayStoreImageCatalog.Phone, "en-US", "phone.png");

        holder.SetImages([image]);
        await Assert.That(holder.Current.Images.Count).IsEqualTo(1);

        holder.ClearSession();
        await Assert.That(holder.Current.Images).IsEmpty();
    }

    [Test]
    public async Task Publish_UploadsSelectedImagesBeforeValidation()
    {
        var path = WritePng();
        var handler = new RecordingHandler();
        var service = new GooglePlayApiService(new HttpClient(handler), new FakeCredentialStore());
        try
        {
            var aab = Path.ChangeExtension(path, ".aab");
            File.Copy(path, aab, overwrite: true);

            var result = await service.PublishAsync(
                "account",
                "com.example.app",
                aab,
                "internal",
                "Release 1",
                [new GooglePlayReleaseNote("en-US", "Notes")],
                100,
                null,
                CancellationToken.None,
                [new GooglePlayStoreImage(GooglePlayStoreImageCatalog.Phone, "en-US", path)]);

            using (Assert.Multiple())
            {
                await Assert.That(result.Succeeded).IsTrue();
                var upload = handler.Requests.Single(request =>
                    request.Uri.Contains("/listings/en-US/phoneScreenshots", StringComparison.Ordinal));
                await Assert.That(upload.Method).IsEqualTo(HttpMethod.Post);
                await Assert.That(upload.ContentType).IsEqualTo("image/png");
                var uploadIndex = handler.Requests.IndexOf(upload);
                var validateIndex = handler.Requests.FindIndex(request =>
                    request.Uri.Contains(":validate", StringComparison.Ordinal));
                await Assert.That(uploadIndex).IsLessThan(validateIndex);
            }
        }
        finally
        {
            File.Delete(path);
            File.Delete(Path.ChangeExtension(path, ".aab"));
        }
    }
}
