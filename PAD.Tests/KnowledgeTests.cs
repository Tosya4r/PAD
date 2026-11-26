using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PAD.Data;
using PAD.Domain;
using Xunit;

public sealed class KnowledgeTests : IDisposable
{
    readonly SqliteConnection _conn = new("Data Source=:memory:");
    readonly PadDbContext _db;

    public KnowledgeTests()
    {
        _conn.Open();
        _db = new PadDbContext(new DbContextOptionsBuilder<PadDbContext>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    [Fact]
    public async Task Notes_Save_Search_Delete()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => KnowledgeService.SaveNoteAsync(_db, null, " ", "x", ""));
        var id = await KnowledgeService.SaveNoteAsync(_db, null, "Договор", "Шаблон договора", "юр");
        await KnowledgeService.SaveNoteAsync(_db, id, "Договор v2", "Новый текст", "юр");
        Assert.Single(await KnowledgeService.NotesAsync(_db));
        Assert.Single(await KnowledgeService.NotesAsync(_db, "новый"));
        Assert.Empty(await KnowledgeService.NotesAsync(_db, "нет такого"));
        await KnowledgeService.DeleteNoteAsync(_db, id);
        Assert.Empty(await KnowledgeService.NotesAsync(_db));
    }

    [Fact]
    public async Task Template_Creates_Project_With_Tasks_And_Exact_Price()
    {
        var cat = await _db.Categories.FirstAsync();
        var tid = await KnowledgeService.SaveTemplateAsync(_db, new ProjectTemplate
        {
            Name = "Сайт-визитка", CategoryId = cat.Id, Price = 1234.56m, Currency = "eur",
            TaskLines = "Дизайн\n\n Вёрстка \nСдача",
        });
        var pid = await KnowledgeService.CreateProjectFromTemplateAsync(_db, tid, null, "Иван", null);

        var p = await ProjectService.GetAsync(_db, pid);
        Assert.Equal("Сайт-визитка", p!.Name);
        Assert.Equal(1234.56m, p.Price);
        Assert.Equal("EUR", p.Currency);
        var tasks = await TaskService.ListAsync(_db, new DateOnly(2026, 11, 26), TaskFilter.All, pid);
        Assert.Equal(3, tasks.Count);
        Assert.Contains(tasks, t => t.Title == "Вёрстка");
    }

    [Fact]
    public async Task Global_Search_Finds_All_Kinds()
    {
        var cat = await _db.Categories.FirstAsync();
        await ProjectService.CreateAsync(_db, new NewProject("Лендинг Орион", "", cat.Id, 100, "EUR", null, ProjectStatus.Planned, ""));
        await TaskService.AddAsync(_db, "Позвонить Орион", null, Priority.Low, null);
        await KnowledgeService.SaveNoteAsync(_db, null, "Орион бриф", "", "");
        await KnowledgeService.SaveTemplateAsync(_db, new ProjectTemplate { Name = "Орион шаблон", Price = 0 });

        var hits = await SearchService.SearchAsync(_db, "орион");
        Assert.Equal(4, hits.Count);
        Assert.Empty(await SearchService.SearchAsync(_db, "  "));
    }
}
