using System.Globalization;
using Avalonia.Data.Converters;

namespace BadgeSmith.Desktop.Services;

public sealed class PlayTime : IValueConverter
{
    public static readonly PlayTime Converter = new();

    public static string Format(double hours) => Math.Round(hours * 60) is var minutes && minutes < 60
        ? $"{minutes} min"
        : $"{hours.ToString("0.0", CultureInfo.CurrentCulture)} h";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double hours ? Format(hours) : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
