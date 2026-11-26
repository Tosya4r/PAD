using System.Windows;

namespace PAD.Desktop.Services;

/// <summary>Переключение тёмной и светлой темы: меняется первый словарь ресурсов приложения.</summary>
public static class ThemeService
{
    public static event Action? ThemeChanged;

    public static void Apply(bool dark)
    {
        var dicts = Application.Current.Resources.MergedDictionaries;
        var uri = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
        var theme = new ResourceDictionary { Source = uri };
        if (dicts.Count > 0) dicts[0] = theme; else dicts.Insert(0, theme);
        ThemeChanged?.Invoke();
    }

    public static System.Windows.Media.Brush Brush(string key, System.Windows.Media.Brush fallback) =>
        Application.Current?.TryFindResource(key) as System.Windows.Media.Brush ?? fallback;
}
