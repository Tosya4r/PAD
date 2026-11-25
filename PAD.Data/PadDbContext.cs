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

        m.Entity<ProjectStatusHistory>(e =>
        {
            e.Property(x => x.OldStatus).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.NewStatus).HasConversion<string>().HasMaxLength(32);
            e.HasOne(x => x.Project).WithMany(p => p.StatusHistory).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
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
            // База старой версии. Пока в ней нет проектов — пересоздаём; иначе данные не трогаем.
            if (Scalar(db, "SELECT COUNT(*) FROM Projects") > 0)
                throw new InvalidOperationException("Схема базы устарела, а в ней есть данные. Автоматическое обновление ещё не реализовано.");
            db.Database.CloseConnection();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            db.Database.EnsureDeleted();
            db.Database.EnsureCreated();
        }
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
