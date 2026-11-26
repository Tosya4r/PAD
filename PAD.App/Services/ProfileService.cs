using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PAD.Desktop.Services;

public sealed class ProfileData
{
    public string Name { get; set; } = "";
    public string Role { get; set; } = "Разработчик";
    public string About { get; set; } = "";
    public string? AvatarFile { get; set; }
    public string StartDate { get; set; } = "";
    public bool Dark { get; set; } = true;
}

/// <summary>Профиль и внешний вид хранятся в files/profile.json и входят в резервную копию вместе с аватаркой.</summary>
public static class ProfileService
{
    static readonly string[] AllowedExt = { ".png", ".jpg", ".jpeg", ".bmp" };

    public static ProfileData Current { get; private set; } = new();
    public static event Action? Changed;

    static string FilePath => Path.Combine(App.FilesDir, "profile.json");

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize<ProfileData>(File.ReadAllText(FilePath)) ?? new ProfileData();
        }
        catch { Current = new ProfileData(); }

        if (string.IsNullOrWhiteSpace(Current.Name)) Current.Name = Environment.UserName;
        if (string.IsNullOrWhiteSpace(Current.StartDate)) Current.StartDate = DateTime.Today.ToString("yyyy-MM-dd");
        Save();
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(App.FilesDir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, FilePath, true);
        }
        catch { /* профиль не критичен для работы */ }
        Changed?.Invoke();
    }

    public static ImageSource? LoadAvatar()
    {
        try
        {
            if (string.IsNullOrEmpty(Current.AvatarFile)) return null;
            var path = Path.Combine(App.FilesDir, Current.AvatarFile);
            if (!File.Exists(path)) return null;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bi.DecodePixelWidth = 256;
            bi.UriSource = new Uri(path);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }

    public static void SetAvatar(string sourcePath)
    {
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (!AllowedExt.Contains(ext)) throw new InvalidOperationException("Поддерживаются изображения PNG, JPG и BMP.");

        Directory.CreateDirectory(App.FilesDir);
        var name = "avatar-" + Guid.NewGuid().ToString("N")[..8] + ext;
        File.Copy(sourcePath, Path.Combine(App.FilesDir, name), true);
        RemoveOldAvatar();
        Current.AvatarFile = name;
        Save();
    }

    public static void ClearAvatar()
    {
        RemoveOldAvatar();
        Current.AvatarFile = null;
        Save();
    }

    static void RemoveOldAvatar()
    {
        try
        {
            if (!string.IsNullOrEmpty(Current.AvatarFile))
                File.Delete(Path.Combine(App.FilesDir, Current.AvatarFile));
        }
        catch { }
    }
}
