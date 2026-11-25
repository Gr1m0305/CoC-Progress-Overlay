using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ClashOverlay.App.Views;

/// <summary>
/// Visible when the value is "truthy": true, non-null, a non-empty string or collection, a non-zero number.
/// ConverterParameter "not" inverts it; "hidden" keeps the space.
/// </summary>
public sealed class VisibleIf : IValueConverter
{
    public static readonly VisibleIf Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var p = parameter as string ?? "";
        var on = value switch
        {
            null => false,
            bool b => b,
            string s => s.Length > 0,
            ICollection c => c.Count > 0,
            int i => i != 0,
            long l => l != 0,
            double d => d != 0,
            _ => true,
        };
        if (p.Contains("not", StringComparison.Ordinal))
        {
            on = !on;
        }

        return on ? Visibility.Visible : p.Contains("hidden", StringComparison.Ordinal) ? Visibility.Hidden : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Enum or value equality for radio buttons: checked when the value equals the parameter.</summary>
public sealed class EqualsParameter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
