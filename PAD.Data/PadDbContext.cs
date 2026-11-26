using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PAD.Domain;

namespace PAD.Data;

/// <summary>
/// Деньги хранятся как целые центы (INTEGER): точно, сортируются и суммируются без float.
/// Значения точнее 0.01 округляются банковским способом.
/// </summary>
public sealed class MoneyConverter : ValueConverter<decimal, long>
{
    public MoneyConverter()
        : base(v => (long)decimal.Round(v * 100m, 0, MidpointRounding.ToEven), v => v / 100m) { }
}

public sealed class PadDbContext : DbContext
{
    public PadDbContext(DbContextOptions<PadDbContext> options) : base(options) { }

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<ProjectDelivery> Deliveries => Set<ProjectDelivery>();
    public DbSet<ProjectPriceHistory> PriceHistory => Set<ProjectPriceHistory>();
    public DbSet<ProjectStatusHistory> StatusHistory => Set<ProjectStatusHistory>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<ProjectTemplate> Templates => Set<ProjectTemplate>();
    public DbSet<ProjectStage> Stages => Set<ProjectStage>();

    protected override void OnModelCreating(ModelBuilder m)
    {
        var money = new MoneyConverter();

        m.Entity<Category>(e =>
        {
            e.Property(x => x.Name).IsRequired().HasMaxLength(100);
            e.Property(x => x.Color).IsRequired().HasMaxLength(7);
            e.HasData(
                new Category { Id = 1, Name = "Лендинги", Color = ColorPalette.Colors[0] },
                new Category { Id = 2, Name = "Корпоративные сайты", Color = ColorPalette.Colors[1] },
                new Category { Id = 3, Name = "Интернет-магазины", Color = ColorPalette.Colors[2] },
                new Category { Id = 4, Name = "Веб-приложения", Color = ColorPalette.Colors[3] },
                new Category { Id = 5, Name = "SaaS", Color = ColorPalette.Colors[4] },
                new Category { Id = 6, Name = "CRM", Color = ColorPalette.Colors[5] },
                new Category { Id = 7, Name = "Индивидуальная разработка", Color = ColorPalette.Colors[6] },
                new Category { Id = 8, Name = "Другие проекты", Color = ColorPalette.Colors[7] });
        });

        m.Entity<Project>(e =>
        {
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.Currency).IsRequired().HasMaxLength(3);
            e.Property(x => x.Price).HasConversion(money);
            e.Property(x => x.PrepaymentAmount).HasConversion(money);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Priority).HasConversion<string>().HasMaxLength(32);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.IsArchived);
        });

        m.Entity<Payment>(e =>
        {
            e.Property(x => x.Amount).HasConversion(money);
            e.Property(x => x.Currency).IsRequired().HasMaxLength(3);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
            e.HasOne(x => x.Project).WithMany(p => p.Payments).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ProjectId, x.ReceivedDate });
        });

        m.Entity<ProjectDelivery>(e =>
        {
            e.Property(x => x.FinalPrice).HasConversion(money);
            e.HasOne(x => x.Project).WithMany(p => p.Deliveries).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<ProjectPriceHistory>(e =>
        {
            e.Property(x => x.OldPrice).HasConversion(money);
            e.Property(x => x.NewPrice).HasConversion(money);
            e.HasOne(x => x.Project).WithMany(p => p.PriceHistory).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<Note>(e =>
        {
            e.ToTable("Notes");
            e.Property(x => x.Title).IsRequired().HasMaxLength(300);
        });

        m.Entity<ProjectTemplate>(e =>
        {
            e.ToTable("Templates");
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.Price).HasConversion(money);
        });

        m.Entity<TaskItem>(e =>
        {
            e.ToTable("Tasks");
            e.Property(x => x.Title).IsRequired().HasMaxLength(300);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Priority).HasConversion<string>().HasMaxLength(32);
            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.Status);
        });

        m.Entity<ProjectStatusHistory>(e =>
        {
            e.Property(x => x.OldStatus).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.NewStatus).HasConversion<string>().HasMaxLength(32);
            e.HasOne(x => x.Project).WithMany(p => p.StatusHistory).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<ProjectStage>(e =>
        {
            e.ToTable("ProjectStages");
            e.Property(x => x.Title).IsRequired().HasMaxLength(200);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Content).HasMaxLength(100000);
            e.HasOne(x => x.Project).WithMany(p => p.Stages).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ProjectId, x.Order });
        });
    }
}

public static class PadDatabase
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PAD", "pad.db");

    public static DbContextOptions<PadDbContext> Options(string path) =>
        new DbContextOptionsBuilder<PadDbContext>().UseSqlite($"Data Source={path}").Options;

    /// <summary>Создаёт базу при первом запуске и включает журнал WAL (устойчивость к аварийному закрытию).</summary>
    public static void Initialize(PadDbContext db)
    {
        db.Database.EnsureCreated();
        if (!ColumnExists(db, "Categories", "Color"))
        {
            // База старой версии: добавляем колонку, данные не трогаем.
            db.Database.ExecuteSqlRaw("ALTER TABLE Categories ADD COLUMN Color TEXT NOT NULL DEFAULT '#2F5BEA'");
            for (var i = 0; i < ColorPalette.Colors.Length && i < 8; i++)
                db.Database.ExecuteSqlRaw("UPDATE Categories SET Color = {0} WHERE Id = {1}", ColorPalette.Colors[i], i + 1);
        }
        db.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS ""Tasks"" (
            ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_Tasks"" PRIMARY KEY AUTOINCREMENT,
            ""Title"" TEXT NOT NULL, ""Notes"" TEXT NOT NULL, ""ProjectId"" INTEGER NULL,
            ""Status"" TEXT NOT NULL, ""Priority"" TEXT NOT NULL, ""Due"" TEXT NULL,
            ""CreatedAt"" TEXT NOT NULL, ""CompletedAt"" TEXT NULL,
            CONSTRAINT ""FK_Tasks_Projects_ProjectId"" FOREIGN KEY (""ProjectId"") REFERENCES ""Projects"" (""Id"") ON DELETE SET NULL)");
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS \"IX_Tasks_Status\" ON \"Tasks\" (\"Status\")");
        db.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS ""Notes"" (
            ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_Notes"" PRIMARY KEY AUTOINCREMENT,
            ""Title"" TEXT NOT NULL, ""Body"" TEXT NOT NULL, ""Tags"" TEXT NOT NULL,
            ""CreatedAt"" TEXT NOT NULL, ""UpdatedAt"" TEXT NOT NULL)");
        db.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS ""Templates"" (
            ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_Templates"" PRIMARY KEY AUTOINCREMENT,
            ""Name"" TEXT NOT NULL, ""Description"" TEXT NOT NULL, ""CategoryId"" INTEGER NULL,
            ""Price"" INTEGER NOT NULL, ""Currency"" TEXT NOT NULL, ""TaskLines"" TEXT NOT NULL)");
        db.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS ""ProjectStages"" (
            ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_ProjectStages"" PRIMARY KEY AUTOINCREMENT,
            ""ProjectId"" INTEGER NOT NULL, ""Order"" INTEGER NOT NULL, ""Title"" TEXT NOT NULL,
            ""Status"" TEXT NOT NULL, ""Content"" TEXT NOT NULL, ""CreatedAt"" TEXT NOT NULL, ""UpdatedAt"" TEXT NOT NULL,
            CONSTRAINT ""FK_ProjectStages_Projects_ProjectId"" FOREIGN KEY (""ProjectId"") REFERENCES ""Projects"" (""Id"") ON DELETE CASCADE)");
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS \"IX_ProjectStages_ProjectId_Order\" ON \"ProjectStages\" (\"ProjectId\", \"Order\")");
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    }

    static long Scalar(PadDbContext db, string sql)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    static bool ColumnExists(PadDbContext db, string table, string column) =>
        Scalar(db, $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}'") > 0;
}
