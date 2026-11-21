using CodePublisher.Features.GooglePlay.Releases;

namespace CodePublisher.Tests.Features.GooglePlay.Releases;

public sealed class GooglePlayReleaseValidationTests
{
    [Test]
    public async Task EmptyNotesAreAllowed()
    {
        await Assert.That(GooglePlayReleaseValidation.ValidateNotes("en-US", string.Empty)).IsNull();
    }

    [Test]
    public async Task NotesOverFiveHundredCharactersAreRejectedBeforeBuild()
    {
        var error = GooglePlayReleaseValidation.ValidateNotes("en-US", new string('x', 501));

        await Assert.That(error).Contains("500");
    }

    [Test]
    public async Task NotesNeedLanguageWhenTextIsPresent()
    {
        var error = GooglePlayReleaseValidation.ValidateNotes("", "Bug fixes");

        await Assert.That(error).Contains("language");
    }
}
