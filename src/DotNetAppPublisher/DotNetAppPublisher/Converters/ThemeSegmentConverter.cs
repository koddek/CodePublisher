using System.Globalization;
using Avalonia.Data.Converters;

namespace DotNetAppPublisher.Converters;

public sealed class ThemeSegmentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string theme && parameter is string target)
        {
            return string.Equals(theme, target, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter is string target)
        {
            return target;
        }

        return Avalonia.AvaloniaProperty.UnsetValue;
    }
}
