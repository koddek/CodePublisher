using CodePublisher.Features.Publishing.Inspect;

namespace CodePublisher.Tests.Features.Publishing.Inspect;

public sealed class EvaluatedPropertyLookupTests
{
    [Test]
    public async Task ReadsTheFirstNamedPropertyWithAValue()
    {
        IReadOnlyDictionary<string, string> values = new Dictionary<string, string>
        {
            ["ApplicationId"] = "  ",
            ["PackageName"] = " com.example.app ",
            ["CFBundleIdentifier"] = "com.other.app"
        };

        await Assert.That(values["ApplicationId", "PackageName", "CFBundleIdentifier"])
            .IsEqualTo("com.example.app");
    }

    [Test]
    public async Task ReturnsNullWhenNoNamedPropertyHasAValue()
    {
        IReadOnlyDictionary<string, string> values = new Dictionary<string, string>
        {
            ["ApplicationId"] = string.Empty
        };

        await Assert.That(values["ApplicationId", "PackageId"]).IsNull();
    }

    [Test]
    public async Task ANullEvaluationResultReadsAsNoValue()
    {
        IReadOnlyDictionary<string, string>? values = null;

        await Assert.That(values["ApplicationId", "PackageId"]).IsNull();
    }

    [Test]
    public async Task FromEvaluated_UsesTheSameLookup()
    {
        IReadOnlyDictionary<string, string> values = new Dictionary<string, string>
        {
            ["ApplicationId"] = "com.example.app"
        };

        await Assert.That(ProjectIdentifier.FromEvaluated(values, "ApplicationId"))
            .IsEqualTo("com.example.app");
    }
}
