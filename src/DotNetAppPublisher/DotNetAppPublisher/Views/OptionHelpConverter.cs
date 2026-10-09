using System.Globalization;
using Avalonia.Data.Converters;

namespace DotNetAppPublisher.Views;

/// <summary>
/// Builds a Build-tab option tooltip from two parts:
/// the static plain-English description passed as <c>ConverterParameter</c>,
/// and the live disabled reason bound from the view model.
/// When the option is available the reason is empty, so only the description shows.
/// </summary>
public sealed class OptionHelpConverter : IValueConverter
{
    public static readonly OptionHelpConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var description = parameter as string ?? string.Empty;
        var reason = value as string;

        if (string.IsNullOrWhiteSpace(reason))
        {
            return description;
        }

        return string.IsNullOrWhiteSpace(description)
            ? reason
            : $@"{description}

{reason}";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Option help tooltips are one-way display text.");
}
