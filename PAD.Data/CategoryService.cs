using Microsoft.EntityFrameworkCore;
using PAD.Domain;

namespace PAD.Data;

public static class CategoryService
{
    /// <summary>Создаёт категорию и автоматически назначает ей свободный цвет палитры.</summary>
    public static async Task<Category> AddAsync(PadDbContext db, string? name, CancellationToken ct = default)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) throw new ArgumentException("Введите название категории.");
        if (name.Length > 100) throw new ArgumentException("Название категории слишком длинное.");

        var existing = await db.Categories.AsNoTracking().ToListAsync(ct);
        if (existing.Any(c => string.Equals(c.Name, name, StringComparison.CurrentCultureIgnoreCase)))
            throw new InvalidOperationException("Такая категория уже есть.");

        var category = new Category { Name = name, Color = ColorPalette.NextFree(existing.Select(c => c.Color)) };
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);
        return category;
    }
}
