using Microsoft.EntityFrameworkCore;
using PAD.Domain;

namespace PAD.Data;

/// <summary>База знаний (заметки) и шаблоны проектов.</summary>
public static class KnowledgeService
{
    public static async Task<List<Note>> NotesAsync(PadDbContext db, string? search = null, CancellationToken ct = default)
    {
        var all = await db.Notes.AsNoTracking().ToListAsync(ct);
        var s = search?.Trim();
        if (!string.IsNullOrEmpty(s))
            all = all.Where(n => Has(n.Title, s) || Has(n.Body, s) || Has(n.Tags, s)).ToList();
        return all.OrderByDescending(n => n.UpdatedAt).ThenByDescending(n => n.Id).ToList();
    }

    public static async Task<int> SaveNoteAsync(PadDbContext db, int? id, string? title, string? body, string? tags, CancellationToken ct = default)
    {
        title = (title ?? "").Trim();
        if (title.Length == 0) throw new ArgumentException("Введите название заметки.");
        Note n;
        if (id is null) { n = new Note(); db.Notes.Add(n); }
        else n = await db.Notes.FindAsync(new object[] { id.Value }, ct) ?? throw new InvalidOperationException("Заметка не найдена.");
        n.Title = title;
        n.Body = body ?? "";
        n.Tags = (tags ?? "").Trim();
        n.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);
        return n.Id;
    }

    public static async Task DeleteNoteAsync(PadDbContext db, int id, CancellationToken ct = default)
    {
        var n = await db.Notes.FindAsync(new object[] { id }, ct);
        if (n is null) return;
        db.Notes.Remove(n);
        await db.SaveChangesAsync(ct);
    }

    public static Task<List<ProjectTemplate>> TemplatesAsync(PadDbContext db, CancellationToken ct = default) =>
        db.Templates.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct);

    public static async Task<int> SaveTemplateAsync(PadDbContext db, ProjectTemplate t, CancellationToken ct = default)
    {
        t.Name = (t.Name ?? "").Trim();
        if (t.Name.Length == 0) throw new ArgumentException("Введите название шаблона.");
        if (t.Price < 0) throw new ArgumentException("Стоимость не может быть отрицательной.");
        if (t.CategoryId is not null && !await db.Categories.AnyAsync(c => c.Id == t.CategoryId, ct))
            throw new ArgumentException("Категория не найдена.");
        t.Currency = string.IsNullOrWhiteSpace(t.Currency) ? "EUR" : t.Currency.Trim().ToUpperInvariant();
        t.TaskLines ??= "";
        t.Description ??= "";
        if (t.Id == 0) db.Templates.Add(t);
        else
        {
            var e = await db.Templates.FindAsync(new object[] { t.Id }, ct) ?? throw new InvalidOperationException("Шаблон не найден.");
            e.Name = t.Name; e.Description = t.Description; e.CategoryId = t.CategoryId;
            e.Price = t.Price; e.Currency = t.Currency; e.TaskLines = t.TaskLines;
        }
        await db.SaveChangesAsync(ct);
        return t.Id;
    }

    public static async Task DeleteTemplateAsync(PadDbContext db, int id, CancellationToken ct = default)
    {
        var t = await db.Templates.FindAsync(new object[] { id }, ct);
        if (t is null) return;
        db.Templates.Remove(t);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Создаёт проект из шаблона вместе с типовыми задачами. Возвращает Id проекта.</summary>
    public static async Task<int> CreateProjectFromTemplateAsync(PadDbContext db, int templateId, string? name, string? client,
        DateOnly? deadline, CancellationToken ct = default)
    {
        var t = await db.Templates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == templateId, ct)
            ?? throw new InvalidOperationException("Шаблон не найден.");
        var catId = t.CategoryId ?? (await db.Categories.OrderBy(c => c.Id).Select(c => (int?)c.Id).FirstOrDefaultAsync(ct))
            ?? throw new InvalidOperationException("Нет ни одной категории.");
        if (!await db.Categories.AnyAsync(c => c.Id == catId, ct))
            catId = await db.Categories.OrderBy(c => c.Id).Select(c => c.Id).FirstAsync(ct);

        var id = await ProjectService.CreateAsync(db, new NewProject(string.IsNullOrWhiteSpace(name) ? t.Name : name!, client ?? "",
            catId, t.Price, t.Currency, deadline, ProjectStatus.Planned, t.Description), ct);

        foreach (var line in t.TaskLines.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            await TaskService.AddAsync(db, line, id, Priority.Medium, null, null, ct);
        return id;
    }

    static bool Has(string? text, string s) => text?.Contains(s, StringComparison.OrdinalIgnoreCase) == true;
}

public enum SearchKind { Project, Task, Note, Template }

public sealed record SearchHit(SearchKind Kind, int Id, string Title, string Sub);

/// <summary>Глобальный поиск по проектам, задачам, заметкам и шаблонам (архивные проекты тоже находятся).</summary>
public static class SearchService
{
    public static async Task<List<SearchHit>> SearchAsync(PadDbContext db, string? query, CancellationToken ct = default)
    {
        var q = query?.Trim();
        var hits = new List<SearchHit>();
        if (string.IsNullOrEmpty(q)) return hits;

        static bool Has(string? t, string s) => t?.Contains(s, StringComparison.OrdinalIgnoreCase) == true;

        foreach (var p in await db.Projects.AsNoTracking().ToListAsync(ct))
            if (Has(p.Name, q) || Has(p.ClientName, q) || Has(p.Description, q))
                hits.Add(new SearchHit(SearchKind.Project, p.Id, p.Name,
                    (string.IsNullOrWhiteSpace(p.ClientName) ? "Без заказчика" : p.ClientName) + (p.IsArchived ? " · в архиве" : "")));
        foreach (var t in await db.Tasks.AsNoTracking().ToListAsync(ct))
            if (Has(t.Title, q) || Has(t.Notes, q))
                hits.Add(new SearchHit(SearchKind.Task, t.Id, t.Title, t.Status == WorkStatus.Done ? "выполнена" : "открыта"));
        foreach (var n in await db.Notes.AsNoTracking().ToListAsync(ct))
            if (Has(n.Title, q) || Has(n.Body, q) || Has(n.Tags, q))
                hits.Add(new SearchHit(SearchKind.Note, n.Id, n.Title, n.Tags));
        foreach (var t in await db.Templates.AsNoTracking().ToListAsync(ct))
            if (Has(t.Name, q) || Has(t.Description, q))
                hits.Add(new SearchHit(SearchKind.Template, t.Id, t.Name, "шаблон проекта"));
        return hits;
    }
}
