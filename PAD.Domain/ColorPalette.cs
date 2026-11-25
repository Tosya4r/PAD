namespace PAD.Domain;

/// <summary>Палитра для категорий. Новая категория получает первый свободный цвет;
/// когда палитра исчерпана — цвета генерируются по золотому углу (всегда различимые оттенки).</summary>
public static class ColorPalette
{
    public static readonly string[] Colors =
    {
        "#2F5BEA", // синий
        "#12A594", // бирюзовый
        "#F5A524", // янтарный
        "#E5484D", // красный
        "#8E4EC6", // фиолетовый
        "#3E8EED", // голубой
        "#D6409F", // розовый
        "#46A758", // зелёный
        "#F76B15", // оранжевый
        "#00A2C7", // циан
        "#6E56CF", // индиго
        "#9BB31A", // лайм
    };

    public static string NextFree(IEnumerable<string> used)
    {
        var taken = new HashSet<string>(used.Select(c => c.ToUpperInvariant()));
        foreach (var c in Colors)
            if (!taken.Contains(c.ToUpperInvariant())) return c;

        for (var i = 0; i < 10_000; i++)
        {
            var hex = HslToHex(i * 137.508 % 360.0, 0.62, 0.50);
            if (!taken.Contains(hex)) return hex;
        }
        return Colors[0];
    }

    static string HslToHex(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = l - c / 2;
        (double r, double g, double b) = (int)(h / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        static int B(double v) => (int)Math.Round(v * 255);
        return $"#{B(r + m):X2}{B(g + m):X2}{B(b + m):X2}";
    }
}
