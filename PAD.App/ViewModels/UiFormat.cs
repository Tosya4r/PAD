using System.Globalization;

namespace PAD.Desktop.ViewModels;

public sealed record MoneyLine(string Amount, string Unit);

public sealed record StatTile(string Label, IReadOnlyList<MoneyLine> Lines, string Glyph, string Accent, string Tint);

/// <summary>Переход между разделами из любого экрана.</summary>
public static class Navigator
{
    public static Action<int>? OpenProject;
    public static Action<string>? Search;
    public static Action? NewProject;
    public static Action<string, int?>? Go;
}

public static class UiFormat
{
    public static string Fmt(decimal v) => v.ToString("#,##0.##", CultureInfo.CurrentCulture);

    public static string Pct(decimal p) => p.ToString("0.#", CultureInfo.CurrentCulture) + " %";

    /// <summary>Полупрозрачный фон плитки того же цвета — одинаково хорошо в тёмной и светлой теме.</summary>
    public static string Tint(string hex) => "#26" + hex.TrimStart('#');

    /// <summary>Валюты не смешиваются: каждая показывается отдельной строкой.</summary>
    public static IReadOnlyList<MoneyLine> Lines(IReadOnlyDictionary<string, decimal> d) =>
        d.Count == 0
            ? new[] { new MoneyLine("0", "EUR") }
            : d.OrderBy(kv => kv.Key).Select(kv => new MoneyLine(Fmt(kv.Value), kv.Key)).ToList();

    public static StatTile Money(string label, IReadOnlyDictionary<string, decimal> d, string glyph, string accent) =>
        new(label, Lines(d), glyph, accent, Tint(accent));

    public static StatTile Count(string label, int n, string glyph, string accent) =>
        new(label, new[] { new MoneyLine(n.ToString(), "") }, glyph, accent, Tint(accent));
}
