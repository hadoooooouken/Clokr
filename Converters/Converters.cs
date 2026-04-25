using System.Globalization;
using System.Windows.Data;
using Clokr.Models;

namespace Clokr.Converters;

/// <summary>
/// Converts BoostMode enum to display string and back.
/// </summary>
public class BoostModeDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is BoostMode mode)
            return mode.ToDisplayString();
        return value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value; // ComboBox SelectedItem binding doesn't need this
    }
}

/// <summary>
/// Converts bool (IsOnAc) to power source indicator text.
/// </summary>
public class PowerSourceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isOnAc)
            return isOnAc ? "🔌 AC Power (Plugged In)" : "🔋 Battery (DC)";
        return "Unknown";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
