using System.Globalization;
using System.Windows.Data;

namespace PAD.Desktop.Converters;

/// <summary>DateTime? ⇄ текст «дд.мм.гггг». Пустая строка — нет даты; неверный ввод игнорируется.</summary>
public sealed class DateTextConverter : IValueConverter
{
    static readonly string[] Formats = { "dd.MM.yyyy", "d.M.yyyy", "dd.MM.yy", "yyyy-MM-dd" };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateTime d ? d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = (value as string)?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        return DateTime.TryParseExact(s, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : Binding.DoNothing;
    }
}
