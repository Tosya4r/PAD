using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;

namespace PAD.Data;

/// <summary>
/// Резервные копии: ZIP с согласованным снимком базы (VACUUM INTO), манифестом и папкой файлов.
/// Копия сначала пишется во временный файл и подменяется атомарно, поэтому недописанных архивов не бывает.
/// </summary>
public static class BackupService
{
    public static string CreateBackup(string dbPath, string destZip, string? filesDir = null)
    {
        var temp = Path.Combine(Path.GetTempPath(), "pad-bk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var tmpZip = destZip + ".tmp";
        try
        {
            var snapshot = Path.Combine(temp, "pad.db");
            using (var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "VACUUM INTO $path";
                cmd.Parameters.AddWithValue("$path", snapshot);
                cmd.ExecuteNonQuery();
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destZip))!);
            if (File.Exists(tmpZip)) File.Delete(tmpZip);
            using (var zip = ZipFile.Open(tmpZip, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(snapshot, "pad.db", CompressionLevel.Optimal);

                var manifest = zip.CreateEntry("manifest.json");
                using (var w = new StreamWriter(manifest.Open(), new UTF8Encoding(false)))
                    w.Write($"{{\"app\":\"PAD\",\"createdAt\":\"{DateTime.Now:s}\",\"format\":1}}");

                if (filesDir is not null && Directory.Exists(filesDir))
                    foreach (var f in Directory.EnumerateFiles(filesDir, "*", SearchOption.AllDirectories))
                        zip.CreateEntryFromFile(f, "files/" + Path.GetRelativePath(filesDir, f).Replace('\\', '/'), CompressionLevel.Optimal);
            }
            File.Move(tmpZip, destZip, overwrite: true);
            return destZip;
        }
        finally
        {
            try { if (File.Exists(tmpZip)) File.Delete(tmpZip); } catch { }
            try { Directory.Delete(temp, true); } catch { }
        }
    }

    /// <summary>Восстановление. Перед заменой текущие данные сохраняются в папку backups. Возвращает путь к этой страховочной копии.</summary>
    public static string Restore(string zipPath, string dbPath, string? filesDir = null)
    {
        var temp = Path.Combine(Path.GetTempPath(), "pad-rs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                var db = zip.GetEntry("pad.db");
                if (db is null || zip.GetEntry("manifest.json") is null)
                    throw new InvalidDataException("Это не резервная копия PAD.");
                db.ExtractToFile(Path.Combine(temp, "pad.db"), true);

                if (filesDir is not null)
                    foreach (var e in zip.Entries.Where(e => e.FullName.StartsWith("files/") && e.Name.Length > 0))
                    {
                        var target = Path.GetFullPath(Path.Combine(filesDir, e.FullName["files/".Length..]));
                        if (!target.StartsWith(Path.GetFullPath(filesDir), StringComparison.OrdinalIgnoreCase)) continue; // защита от ../
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        e.ExtractToFile(target, true);
                    }
            }

            var extracted = Path.Combine(temp, "pad.db");
            if (IntegrityCheck(extracted) != "ok")
                throw new InvalidDataException("Файл копии повреждён: проверка целостности не пройдена.");
            if (!HasTable(extracted, "Projects") || !HasTable(extracted, "Payments"))
                throw new InvalidDataException("В копии нет данных PAD.");

            var safety = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath))!, "backups",
                $"before-restore-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            if (File.Exists(dbPath)) CreateBackup(dbPath, safety, filesDir);

            SqliteConnection.ClearAllPools();
            foreach (var ext in new[] { "-wal", "-shm" })
                if (File.Exists(dbPath + ext)) File.Delete(dbPath + ext);
            File.Copy(extracted, dbPath, true);
            return safety;
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { }
        }
    }

    public static string IntegrityCheck(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check";
        return Convert.ToString(cmd.ExecuteScalar()) ?? "unknown";
    }

    static bool HasTable(string dbPath, string table)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$n";
        cmd.Parameters.AddWithValue("$n", table);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    /// <summary>Одна автокопия в день, хранится последние <paramref name="keep"/> штук.</summary>
    public static void AutoBackup(string dbPath, string backupsDir, int keep = 7, DateOnly? today = null, string? filesDir = null)
    {
        var day = today ?? DateOnly.FromDateTime(DateTime.Today);
        var target = Path.Combine(backupsDir, $"auto-{day:yyyyMMdd}.zip");
        if (!File.Exists(target)) CreateBackup(dbPath, target, filesDir);

        var old = Directory.GetFiles(backupsDir, "auto-*.zip").OrderByDescending(Path.GetFileName).Skip(keep);
        foreach (var f in old)
            try { File.Delete(f); } catch { }
    }
}
