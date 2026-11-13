using System.Text;
using System.Text.Json;

namespace TDNControl;

public static class AppInfo
{
    public const string Version = "1.0.5";
    public const string Name = "TDN Control";
    public const string Company = "TDN Media";
    public const string SelfFolderName = "TDN_Control";
    public const string SelfExeName = "TDN_Control.exe";
}

/// <summary>Cách cập nhật một app từ thư mục LAN.</summary>
public enum UpdateKind
{
    Installer,   // chạy bộ cài (Inno /SILENT hoặc NSIS /S)
    ReplaceExe,  // thay file exe đang cài (vd Render Music Pro)
    Portable,    // exe chạy thẳng: chép về máy rồi mở
    Script,      // chạy script cập nhật có sẵn trong thư mục (vd tiện ích Chrome)
}

/// <summary>version.json trong mỗi thư mục app của thư mục cập nhật.</summary>
public sealed class UpdateManifest
{
    public string Version { get; set; } = "";
    public string File { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Notes { get; set; } = "";
    /// <summary>Có giá trị khi bản này đọc được từ GitHub Releases (dự phòng) thay vì thư mục LAN.</summary>
    public string? DownloadUrl { get; set; }
}

/// <summary>tdn-app.json (tùy chọn) đặt cạnh version.json để khai báo app mới mà không cần sửa code.</summary>
public sealed class AppOverride
{
    public string? Name { get; set; }
    public string? Kind { get; set; }        // installer | replace | portable | script
    public string? ExeName { get; set; }     // tên file exe sau khi cài
    public string? Match { get; set; }       // chữ dùng để tìm trong danh sách phần mềm đã cài
    public string? Script { get; set; }      // file script cập nhật (Script)
    public string? LaunchCommand { get; set; } // lệnh mở thay cho exe
    public bool Hidden { get; set; }         // true: không hiện trên bảng điều khiển
}

public sealed class AppEntry
{
    public string Id = "";                // tên thư mục trong CAP_NHAT
    public string Name = "";
    public string Folder = "";            // thư mục app trong CAP_NHAT
    public UpdateKind Kind;
    public string ExeName = "";
    public string Match = "";
    public string? Script;
    public string? LaunchCommand;
    public UpdateManifest? Latest;
    public string? LatestError;

    // Trạng thái trên máy
    public string? InstalledExe;
    public string? InstalledVersion;
    public string? RegistryKey;           // tên khóa gỡ cài đặt (để biết Inno hay NSIS)
    public bool Running;

    public bool Hidden;

    public bool IsInstalled => InstalledExe != null;
    /// <summary>Đã cài và bản trên LAN mới hơn (không đọc được phiên bản thì không báo).</summary>
    public bool HasUpdate => IsInstalled && Latest != null && InstalledVersion != null &&
        VersionUtil.Compare(Latest.Version, InstalledVersion) > 0;
}

public sealed class ControlSettings
{
    public string UpdateFolder { get; set; } = @"D:\Code\CAP_NHAT";
    public bool StartWithWindows { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool NotifyUpdates { get; set; } = true;
    /// <summary>Thư mục app trong CAP_NHAT không hiện trên bảng điều khiển.</summary>
    public List<string> HiddenApps { get; set; } = new();
    public Dictionary<string, string> ExeOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Phiên bản đã cài qua TDN Control (cho app không ghi phiên bản trong exe).</summary>
    public Dictionary<string, string> InstalledByControl { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class Store
{
    public static string DataDir
    {
        get
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TDN Control");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string InstallDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "TDN Control");

    public static string PortableAppsDir => Path.Combine(DataDir, "apps");
    private static string SettingsFile => Path.Combine(DataDir, "settings.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static ControlSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var s = JsonSerializer.Deserialize<ControlSettings>(File.ReadAllText(SettingsFile, Encoding.UTF8), Options);
                if (s != null)
                {
                    s.ExeOverrides = new(s.ExeOverrides ?? new(), StringComparer.OrdinalIgnoreCase);
                    s.InstalledByControl = new(s.InstalledByControl ?? new(), StringComparer.OrdinalIgnoreCase);
                    return s;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { Log.Write("Đọc cài đặt lỗi: " + ex.Message); }
        return new ControlSettings();
    }

    public static void Save(ControlSettings settings)
    {
        try
        {
            string temp = SettingsFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options), new UTF8Encoding(false));
            File.Move(temp, SettingsFile, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Write("Lưu cài đặt lỗi: " + ex.Message); }
    }

    public static T? ReadJson<T>(string path) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8), Options); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }
}

public static class Log
{
    public static void Write(string text)
    {
        try
        {
            string dir = Path.Combine(Store.DataDir, "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, $"tdn-control-{DateTime.Now:yyyyMMdd}.log"),
                $"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}", new UTF8Encoding(false));
        }
        catch { }
    }
}

public static class VersionUtil
{
    public static Version? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var digits = System.Text.RegularExpressions.Regex.Match(text, @"\d+(\.\d+){0,3}");
        if (!digits.Success) return null;
        var parts = digits.Value.Split('.').Select(p => int.TryParse(p, out int n) ? n : 0).ToList();
        while (parts.Count < 4) parts.Add(0);
        return new Version(parts[0], parts[1], parts[2], parts[3]);
    }

    public static int Compare(string? a, string? b)
    {
        var va = Parse(a); var vb = Parse(b);
        if (va == null && vb == null) return 0;
        if (va == null) return -1;
        if (vb == null) return 1;
        return va.CompareTo(vb);
    }

    public static string Short(string? text)
    {
        var v = Parse(text);
        if (v == null) return text ?? "";
        return v.Revision > 0 ? v.ToString(4) : v.ToString(3);
    }
}
