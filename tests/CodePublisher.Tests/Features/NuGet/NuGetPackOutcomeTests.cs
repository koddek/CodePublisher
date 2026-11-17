using CodePublisher.Features.NuGet.Models;
using CodePublisher.Features.NuGet.Services;

namespace CodePublisher.Tests.Features.NuGet;

public sealed class NuGetPackOutcomeTests
{
    [Test]
    public async Task Packed_CarriesThePackageAndDerivesSuccessFromValidation()
    {
        var validation = new NuGetValidationReport(
            "Lib.1.0.0.nupkg", new NuGetPackageIdentity("Lib", "1.0.0"), [], []);
        var outcome = NuGetPackOutcome.Packed(new NuGetPackResult("Lib.1.0.0.nupkg", validation));

        using (Assert.Multiple())
        {
            await Assert.That(outcome.Success).IsTrue();
            await Assert.That(outcome.Package).IsNotNull();
            await Assert.That(outcome.Message).IsEqualTo("Packed Lib.1.0.0.nupkg.");
        }
    }

    [Test]
    public async Task Packed_WithAnInvalidPackage_ReportsFailureButKeepsTheArtifact()
    {
        var issue = new NuGetValidationIssue(NuGetIssueSeverity.Error, "bad nuspec");
        var validation = new NuGetValidationReport("Lib.1.0.0.nupkg", null, [], [issue]);
        var outcome = NuGetPackOutcome.Packed(new NuGetPackResult("Lib.1.0.0.nupkg", validation));

        using (Assert.Multiple())
        {
            await Assert.That(outcome.Success).IsFalse();
            await Assert.That(outcome.Package).IsNotNull();
            await Assert.That(outcome.Message).IsEqualTo(
                "Packed Lib.1.0.0.nupkg, but the package failed validation.");
        }
    }

    [Test]
    public async Task Failed_HasNoPackageAndKeepsTheReason()
    {
        var outcome = NuGetPackOutcome.Failed("dotnet pack failed");

        using (Assert.Multiple())
        {
            await Assert.That(outcome.Success).IsFalse();
            await Assert.That(outcome.Package).IsNull();
            await Assert.That(outcome.Message).IsEqualTo("dotnet pack failed");
        }
    }

    [Test]
    public async Task Default_DoesNotThrowWhenRead()
    {
        // A default union holds null contents. The null arm in each member keeps reads safe.
        NuGetPackOutcome outcome = default;

        using (Assert.Multiple())
        {
            await Assert.That(outcome.Success).IsFalse();
            await Assert.That(outcome.Package).IsNull();
            await Assert.That(outcome.Message).IsEqualTo("Pack did not produce a result.");
        }
    }
}
