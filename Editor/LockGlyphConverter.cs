using System.Globalization;
using System.Windows.Data;

namespace SandcastleRpgMaker.Editor;

/// <summary>True (built-in stat) -> a lock glyph; false (custom stat) -> nothing. One-way only, never used for input.</summary>
public class LockGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "\U0001F512" : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
