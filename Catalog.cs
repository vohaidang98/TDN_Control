using System.Diagnostics;
using Microsoft.Win32;

namespace TDNControl;

/// <summary>Đọc danh sách app từ thư mục cập nhật LAN và tìm app đã cài trên máy.</summary>
public static class Catalog
{
    private sealed record Known(string Name, UpdateKind Kind, string ExeName, string Match, string? LaunchCommand = null);

    // App đã biết. App mới chỉ cần thêm thư mục vào CAP_NHAT (tự đoán), hoặc thêm tdn-app.json để khai báo rõ.
    private static readonly Dictionary<string, Known> KnownApps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RenderMusicPro"] = new("Render Music Pro", UpdateKind.ReplaceExe, "RenderMusicPro.exe", "rendermusicpro"),
        ["ClipForge"] = new("ClipForge Studio", UpdateKind.Installer, "ClipForge.exe", "clipforge"),
        ["ClipMerge"] = new("ClipMerge", UpdateKind.Installer, "ClipMerge.exe", "clipmerge"),
        ["GetAudio"] = new("Gen Audio", UpdateKind.Portable, "", "genaudio"),
        ["MuseLandscapeStudio"] = new("Muse Landscape Studio", UpdateKind.Installer, "MuseLandscapeStudio.exe", "muselandscapestudio"),
        ["TD-Browser"] = new("TD Browser", UpdateKind.Installer, "TD Browser.exe", "tdbrowser"),
    };

    public static List<AppEntry> Scan(ControlSettings settings)
    {
        var list = new List<AppEntry>();
        string root = settings.UpdateFolder;
        if (!Directory.Exists(root)) return list;
        var hidden = new HashSet<string>(settings.HiddenApps ?? new(), StringComparer.OrdinalIgnoreCase);
        var installed = ReadUninstallEntries();

        foreach (string folder in Directory.GetDirectories(root).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            string id = Path.GetFileName(folder);
            if (id.StartsWith('_') || id.StartsWith('.') || hidden.Contains(id)) continue;
            if (id.Equals(AppInfo.SelfFolderName, StringComparison.OrdinalIgnoreCase)) continue;
            string manifestPath = Path.Combine(folder, "version.json");
            if (!File.Exists(manifestPath)) continue;

            var entry = new AppEntry { Id = id, Folder = folder };
            entry.Latest = Store.ReadJson<UpdateManifest>(manifestPath);
            if (entry.Latest == null || string.IsNullOrWhiteSpace(entry.Latest.File))
            {
                entry.LatestError = "version.json lỗi";
                entry.Latest = null;
            }
            ApplyDefinition(entry, Store.ReadJson<AppOverride>(Path.Combine(folder, "tdn-app.json")));
            if (entry.Hidden) continue;
            DetectInstalled(entry, settings, installed);
            list.Add(entry);
        }
        RefreshRunning(list);
        return list;
    }

    private static void ApplyDefinition(AppEntry entry, AppOverride? over)
    {
        string file = entry.Latest?.File ?? "";
        if (KnownApps.TryGetValue(entry.Id, out var known))
        {
            entry.Name = known.Name; entry.Kind = known.Kind; entry.ExeName = known.ExeName;
            entry.Match = known.Match; entry.LaunchCommand = known.LaunchCommand;
        }
        else
        {
            // Tự đoán cho app mới: tên từ thư mục, loại cập nhật từ tên file trong version.json.
            entry.Name = System.Text.RegularExpressions.Regex.Replace(entry.Id.Replace('_', ' ').Replace('-', ' '),
                "([a-z])([A-Z])", "$1 $2");
            entry.Match = Normalize(entry.Id);
            bool setup = file.Contains("setup", StringComparison.OrdinalIgnoreCase) || file.Contains("install", StringComparison.OrdinalIgnoreCase);
            entry.Kind = file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !setup ? UpdateKind.Portable : UpdateKind.Installer;
            if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) entry.Kind = UpdateKind.Script;
            entry.ExeName = "";
        }
        if (over != null)
        {
            if (over.Hidden) { entry.Hidden = true; return; }
            if (!string.IsNullOrWhiteSpace(over.Name)) entry.Name = over.Name!;
            if (!string.IsNullOrWhiteSpace(over.ExeName)) entry.ExeName = over.ExeName!;
            if (!string.IsNullOrWhiteSpace(over.Match)) entry.Match = Normalize(over.Match!);
            if (!string.IsNullOrWhiteSpace(over.Script)) entry.Script = over.Script;
            if (!string.IsNullOrWhiteSpace(over.LaunchCommand)) entry.LaunchCommand = over.LaunchCommand;
            entry.Kind = (over.Kind ?? "").ToLowerInvariant() switch
            {
                "installer" => UpdateKind.Installer,
                "replace" => UpdateKind.ReplaceExe,
                "portable" => UpdateKind.Portable,
                "script" => UpdateKind.Script,
                _ => entry.Kind
            };
        }
        if (entry.Kind == UpdateKind.Script && entry.Script == null)
            entry.Script = Directory.GetFiles(entry.Folder, "*.bat").FirstOrDefault() is string bat ? Path.GetFileName(bat) : null;
    }

    public static string Normalize(string text) =>
        new string(text.ToLowerInvariant().Where(char.IsLetter).ToArray());

    private sealed record Uninstall(string Key, string Name, string? Version, string? Icon, string? Location);

    private static List<Uninstall> ReadUninstallEntries()
    {
        var result = new List<Uninstall>();
        void Read(RegistryKey hive, string path)
        {
            try
            {
                using var key = hive.OpenSubKey(path);
                if (key == null) return;
                foreach (string sub in key.GetSubKeyNames())
                {
                    using var k = key.OpenSubKey(sub);
                    if (k?.GetValue("DisplayName") is not string name) continue;
                    result.Add(new Uninstall(sub, name, k.GetValue("DisplayVersion") as string,
                        k.GetValue("DisplayIcon") as string, k.GetValue("InstallLocation") as string));
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        }
        const string uninstall = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
        Read(Registry.CurrentUser, uninstall);
        Read(Registry.LocalMachine, uninstall);
        Read(Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall");
        return result;
    }

    private static void DetectInstalled(AppEntry entry, ControlSettings settings, List<Uninstall> installed)
    {
        entry.InstalledExe = null; entry.InstalledVersion = null; entry.RegistryKey = null;
        string? exe = null;
        string? registryVersion = null;

        // 1) Đường dẫn người dùng tự chọn.
        if (settings.ExeOverrides.TryGetValue(entry.Id, out string? manual) && File.Exists(manual)) exe = manual;

        // 2) Danh sách phần mềm đã cài của Windows.
        var hit = installed.FirstOrDefault(u => entry.Match.Length > 0 && Normalize(u.Name).StartsWith(entry.Match, StringComparison.Ordinal));
        if (hit != null)
        {
            entry.RegistryKey = hit.Key;
            registryVersion = hit.Version;
            if (exe == null)
            {
                string? icon = hit.Icon?.Split(',')[0].Trim('"', ' ');
                if (icon != null && icon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(icon)
                    && !Path.GetFileName(icon).StartsWith("unins", StringComparison.OrdinalIgnoreCase)) exe = icon;
                else if (hit.Location != null && entry.ExeName.Length > 0 && File.Exists(Path.Combine(hit.Location, entry.ExeName)))
                    exe = Path.Combine(hit.Location, entry.ExeName);
            }
        }

        // 3) App chạy thẳng đã chép về qua TDN Control.
        if (exe == null && entry.Kind == UpdateKind.Portable)
        {
            string dir = Path.Combine(Store.PortableAppsDir, entry.Id);
            if (Directory.Exists(dir))
            {
                // Tên cố định trước; bản cài kiểu cũ (exe khác tên) vẫn nhận diện được.
                string fixedExe = Path.Combine(dir, Actions.PortableExeName(entry));
                exe = File.Exists(fixedExe) ? fixedExe : Directory.GetFiles(dir, "*.exe").FirstOrDefault();
            }
        }

        // 4) Vị trí cài thường gặp.
        if (exe == null && entry.ExeName.Length > 0)
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            foreach (string baseDir in new[] { Path.Combine(local, "Programs"), pf })
            {
                if (!Directory.Exists(baseDir)) continue;
                foreach (string dir in Directory.GetDirectories(baseDir))
                {
                    string candidate = Path.Combine(dir, entry.ExeName);
                    if (File.Exists(candidate)) { exe = candidate; break; }
                }
                if (exe != null) break;
            }
        }

        entry.InstalledExe = exe;
        if (exe == null && registryVersion == null) return;

        // Phiên bản: trong exe > ghi nhận khi TDN Control cài > danh sách phần mềm.
        string? fileVersion = null;
        if (exe != null)
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(exe);
                fileVersion = VersionUtil.Parse(info.ProductVersion) != null ? info.ProductVersion
                    : VersionUtil.Parse(info.FileVersion) != null ? info.FileVersion : null;
            }
            catch { }
        }
        settings.InstalledByControl.TryGetValue(entry.Id, out string? recorded);
        entry.InstalledVersion = new[] { fileVersion, recorded, registryVersion }
            .Where(v => VersionUtil.Parse(v) != null)
            .OrderByDescending(v => VersionUtil.Parse(v))
            .FirstOrDefault();
        if (entry.InstalledVersion != null) entry.InstalledVersion = VersionUtil.Short(entry.InstalledVersion);
    }

    public static void RefreshRunning(IEnumerable<AppEntry> apps)
    {
        foreach (var app in apps)
        {
            app.Running = false;
            if (app.InstalledExe == null) continue;
            app.Running = IsRunning(app);
        }
    }

    /// <summary>Đọc trực tiếp xem app có đang chạy không (và giải phóng các Process đã mở).</summary>
    public static bool IsRunning(AppEntry app)
    {
        var list = GetProcesses(app);
        try { return list.Count > 0; }
        finally { foreach (var p in list) p.Dispose(); }
    }

    public static List<Process> GetProcesses(AppEntry app)
    {
        var result = new List<Process>();
        if (app.InstalledExe == null) return result;
        string name = Path.GetFileNameWithoutExtension(app.InstalledExe);
        foreach (var p in Process.GetProcessesByName(name))
        {
            try
            {
                string? path = p.MainModule?.FileName;
                if (path == null || string.Equals(Path.GetFullPath(path), Path.GetFullPath(app.InstalledExe), StringComparison.OrdinalIgnoreCase))
                { result.Add(p); continue; }
            }
            catch { result.Add(p); continue; } // không đọc được đường dẫn (khác quyền): coi như đúng app
            p.Dispose();
        }
        return result;
    }
}
