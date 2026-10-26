using System.Net;
using System.Net.Http;
using System.Text;
using CodePublisher.Features.GooglePlay.Authentication;
using CodePublisher.Features.GooglePlay.Models;
using CodePublisher.Features.GooglePlay.Services;

namespace CodePublisher.Tests.Features.GooglePlay.Services;

/// <summary>
/// The Google Play API reports failures in its own words. These tests pin the extra guidance the
/// publisher adds, because the bare message does not say what to do next, and two of these errors
/// are the ones that regularly block moving a release between lines.
/// </summary>
public sealed class GooglePlayApiServiceErrorTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public FakeHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
    }

    private sealed class FakeCredentialStore : IGoogleCredentialStore
    {
        public GooglePlayAccountInfo Add(GoogleOAuthTokens tokens, GooglePlayAccountInfo account) => account;

        public Task<string?> GetAccessTokenAsync(string accountId, CancellationToken cancellationToken) =>
            Task.FromResult<string?>("token");

        public Task RevokeAsync(string accountId, CancellationToken cancellationToken) => Task.CompletedTask;

        public void Forget(string accountId)
        {
        }
    }

    private static GooglePlayApiService CreateService(HttpStatusCode statusCode, string body) =>
        new(
            new HttpClient(new FakeHandler(statusCode, body)),
            new FakeCredentialStore());

    [Test]
    public async Task AUsedVersionCodeExplainsThatPlayNeverReusesOne()
    {
        var service = CreateService(
            HttpStatusCode.Forbidden,
            """{"error":{"code":403,"message":"Version code 42 has already been used.","status":"PERMISSION_DENIED"}}""");

        var error = await Assert.That(async () =>
            await service.GetUsedVersionCodesAsync("account", "com.example.app", CancellationToken.None))
            .Throws<InvalidOperationException>();

        using (Assert.Multiple())
        {
            await Assert.That(error!.Message).Contains("already been used");
            await Assert.That(error.Message).Contains("never reuses one");
            await Assert.That(error.Message).Contains("Raise the version code");
        }
    }

    [Test]
    public async Task AChangeAlreadyInReviewExplainsThatItHasToFinishFirst()
    {
        var service = CreateService(
            HttpStatusCode.BadRequest,
            """{"error":{"code":400,"message":"You already have changes in review.","status":"FAILED_PRECONDITION"}}""");

        var error = await Assert.That(async () =>
            await service.GetUsedVersionCodesAsync("account", "com.example.app", CancellationToken.None))
            .Throws<InvalidOperationException>();

        using (Assert.Multiple())
        {
            await Assert.That(error!.Message).Contains("changes in review");
            await Assert.That(error.Message).Contains("Wait for it to finish");
        }
    }

    [Test]
    public async Task AShadowedReleaseExplainsThatAHigherVersionCodeExists()
    {
        var service = CreateService(
            HttpStatusCode.BadRequest,
            """{"error":{"code":400,"message":"multiApkShadowedActiveApk: Version 1123 of this app can not be downloaded by any devices.","status":"FAILED_PRECONDITION"}}""");

        var error = await Assert.That(async () =>
            await service.GetUsedVersionCodesAsync("account", "com.example.app", CancellationToken.None))
            .Throws<InvalidOperationException>();

        using (Assert.Multiple())
        {
            await Assert.That(error!.Message).Contains("multiApkShadowedActiveApk");
            await Assert.That(error.Message).Contains("higher version code");
        }
    }

    [Test]
    public async Task AnUnrelatedErrorKeepsTheGoogleMessage()
    {
        var service = CreateService(
            HttpStatusCode.InternalServerError,
            """{"error":{"code":500,"message":"Something went wrong on Google's side.","status":"INTERNAL"}}""");

        var error = await Assert.That(async () =>
            await service.GetUsedVersionCodesAsync("account", "com.example.app", CancellationToken.None))
            .Throws<InvalidOperationException>();

        using (Assert.Multiple())
        {
            await Assert.That(error!.Message).Contains("Something went wrong on Google's side.");
            await Assert.That(error.Message).DoesNotContain("Raise the version code");
        }
    }
}
