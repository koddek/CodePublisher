using System.Globalization;
using Avalonia.Data.Converters;

namespace DotNetAppPublisher.Views;

/// <summary>
/// Dims a step label that pairing has not reached yet. The value is the step number the view model
/// holds and the parameter is the step this label stands for, so a completed step stays readable
/// while the ones ahead of it wait.
/// </summary>
public sealed class StepReachedConverter : IValueConverter
{
    public static readonly StepReachedConverter Instance = new();

    private const double Reached = 1.0;
    private const double Waiting = 0.4;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int step
        && parameter is string text
        && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var target)
            ? step >= target ? Reached : Waiting
            : Reached;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
