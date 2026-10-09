using System.Globalization;
using DotNetAppPublisher.Views;

namespace DotNetAppPublisher.Tests.Views;

public sealed class OptionHelpConverterTests
{
    private const string Description = "Plain-English description of the option.";
    private const string Reason = "Disabled: this option needs another setting first.";

    [Test]
    public async Task Convert_WithEmptyReason_ReturnsDescriptionOnly()
    {
        var result = OptionHelpConverter.Instance.Convert(
            string.Empty, typeof(string), Description, CultureInfo.InvariantCulture);

        await Assert.That(result).IsEqualTo(Description);
    }

    [Test]
    public async Task Convert_WithWhitespaceReason_ReturnsDescriptionOnly()
    {
        var result = OptionHelpConverter.Instance.Convert(
            "   ", typeof(string), Description, CultureInfo.InvariantCulture);

        await Assert.That(result).IsEqualTo(Description);
    }

    [Test]
    public async Task Convert_WithDisabledReason_AppendsReasonOnNewLine()
    {
        var result = OptionHelpConverter.Instance.Convert(
            Reason, typeof(string), Description, CultureInfo.InvariantCulture);

        await Assert.That(result).IsEqualTo($"{Description}\n\n{Reason}");
    }

    [Test]
    public async Task Convert_WithoutDescription_ReturnsReasonOnly()
    {
        var result = OptionHelpConverter.Instance.Convert(
            Reason, typeof(string), null, CultureInfo.InvariantCulture);

        await Assert.That(result).IsEqualTo(Reason);
    }

    [Test]
    public async Task ConvertBack_ThrowsBecauseHelpTextIsOneWay()
    {
        await Assert.That(() => OptionHelpConverter.Instance.ConvertBack(
                Description, typeof(string), null, CultureInfo.InvariantCulture))
            .Throws<NotSupportedException>();
    }
}
