using DotNetAppPublisher.Features.GooglePlay.Setup;

namespace DotNetAppPublisher.Tests.Features.GooglePlay.Setup;

public sealed class EnvironmentVariableServiceTests
{
    [Test]
    public async Task KnownDefinitions_ProvideHumanLabelsAndExamples()
    {
        var definitions = new EnvironmentVariableService().GetKnownVariableDefinitions();

        using (Assert.Multiple())
        {
            await Assert.That(definitions.Count()).IsEqualTo(3);
            await Assert.That(definitions[0].DisplayName).IsEqualTo("Google Desktop OAuth client ID");
            await Assert.That(definitions[0].Example).Contains("apps.googleusercontent.com");
            await Assert.That(definitions[1].DisplayName).IsEqualTo("Java home");
            await Assert.That(definitions[2].DisplayName).IsEqualTo("Android SDK location");
        }
    }

    [Test]
    public async Task KnownDefinitions_ExplainWhereToGetEachValue()
    {
        var definitions = new EnvironmentVariableService().GetKnownVariableDefinitions();

        using (Assert.Multiple())
        {
            await Assert.That(definitions[0].SetupInstructions).Contains("Google Cloud Console");
            await Assert.That(definitions[0].HelpUrl).IsEqualTo("https://console.cloud.google.com/apis/credentials");
            await Assert.That(definitions[1].SetupInstructions).Contains("JDK 17 or 21");
            await Assert.That(definitions[1].HelpUrl).IsEqualTo("https://adoptium.net/temurin/releases/");
            await Assert.That(definitions[2].SetupInstructions).Contains("Android Studio");
            await Assert.That(definitions[2].HelpUrl).IsEqualTo("https://developer.android.com/studio");
        }
    }
}
