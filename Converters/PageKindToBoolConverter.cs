using System.Globalization;
using System.Windows.Data;

namespace JellyfinWhisperCommand.Converters;

public sealed class PageKindToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not PageKind current || parameter is not string expected) return false;
        return string.Equals(current.ToString(), expected, StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (true.Equals(value) && parameter is string expected &&
            Enum.TryParse<PageKind>(expected, ignoreCase: true, out var page))
            return page;
        return Binding.DoNothing;
    }
}
