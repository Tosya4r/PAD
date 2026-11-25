using System.IO.Compression;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PAD.Data;
using PAD.Domain;
using Xunit;

public sealed class BackupTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "pad-test-" + Guid.NewGuid().ToString("N"));
    string DbPath => Path.Combine(_dir, "pad.db");

    public BackupTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    PadDbContext Open()
    {
        var db = new PadDbContext(PadDatabase.Options(DbPath));
        PadDatabase.Initialize(db);
        return db;
    }

    static void AddProject(PadDbContext db, string name, decimal paid = 0)
    {
        var p = new Project { Name = name, CategoryId = 1, Price = 1000m };
        if (paid > 0)
            p.Payments.Add(new Payment { Amount = paid, Currency = "EUR", ReceivedDate = new DateOnly(2026, 11, 1), Type = PaymentType.Prepayment });
        db.Projects.Add(p);
        db.SaveChanges();
    }

    [Fact]
    public void Backup_Then_Restore_Returns_Old_State_And_Keeps_Safety_Copy()
    {
        using (var db = Open()) AddProject(db, "A", 500m);
        var zip = Path.Combine(_dir, "b.zip");
        BackupService.CreateBackup(DbPath, zip);
        using (var db = Open()) AddProject(db, "B");

        var safety = BackupService.Restore(zip, DbPath);

        using (var db = Open())
        {
            Assert.Equal(new[] { "A" }, db.Projects.Select(p => p.Name).ToArray());
            Assert.Equal(500m, db.Payments.Single().Amount);
        }
        Assert.True(File.Exists(safety));   // состояние до восстановления не потеряно
        Assert.Equal("ok", BackupService.IntegrityCheck(DbPath));
    }

    [Fact]
    public void Restore_Rejects_Foreign_And_Broken_Files()
    {
        using (var db = Open()) AddProject(db, "A");

        var notZip = Path.Combine(_dir, "x.zip");
        File.WriteAllText(notZip, "not a zip");
        Assert.ThrowsAny<Exception>(() => BackupService.Restore(notZip, DbPath));

        var foreign = Path.Combine(_dir, "f.zip");
        using (var z = ZipFile.Open(foreign, ZipArchiveMode.Create)) z.CreateEntry("foo.txt");
        Assert.Throws<InvalidDataException>(() => BackupService.Restore(foreign, DbPath));

        using var db2 = Open();
        Assert.Single(db2.Projects.ToList());   // текущие данные не тронуты
    }

    [Fact]
    public void AutoBackup_Once_Per_Day_And_Prunes_Old()
    {
        using (var db = Open()) AddProject(db, "A");
        var dir = Path.Combine(_dir, "backups");
        Directory.CreateDirectory(dir);
        for (var i = 1; i <= 9; i++) File.WriteAllText(Path.Combine(dir, $"auto-2025010{i}.zip"), "old");

        BackupService.AutoBackup(DbPath, dir, 7, new DateOnly(2026, 11, 25));
        BackupService.AutoBackup(DbPath, dir, 7, new DateOnly(2026, 11, 25));

        var files = Directory.GetFiles(dir, "auto-*.zip");
        Assert.Equal(7, files.Length);
        Assert.Contains(files, f => f.EndsWith("auto-20261125.zip"));
    }

    [Fact]
    public async Task Csv_Export_Escapes_And_Signs_Refunds()
    {
        using var db = Open();
        var p = new Project { Name = "Shop; \"X\"", CategoryId = 3, Price = 100m };
        p.Payments.Add(new Payment { Amount = 100m, Currency = "EUR", ReceivedDate = new DateOnly(2026, 11, 1), Type = PaymentType.Final });
        p.Payments.Add(new Payment { Amount = 30m, Currency = "EUR", ReceivedDate = new DateOnly(2026, 11, 2), Type = PaymentType.Refund });
        db.Projects.Add(p);
        await db.SaveChangesAsync();

        var csv = await ExportService.PaymentsCsvAsync(db);
        var lines = csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("Дата;Проект", lines[0]);
        Assert.Contains("\"Shop; \"\"X\"\"\"", lines[1]);
        Assert.Contains(";-30,00;EUR;", lines[2]);
    }
}
