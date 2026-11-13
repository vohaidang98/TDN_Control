using System.Diagnostics;
using System.Net.Http;
using System.Text;
using Microsoft.Win32;

namespace TDNControl;

/// <summary>Chạy cùng Windows (khóa Run của người dùng, không cần quyền quản trị).</summary>
public static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TDN Control";

    /// <summary>Giá trị khóa Run: luôn trỏ vào exe ĐÃ CÀI (không bao giờ vào file tạm đang chạy).</summary>
    public static string RunValue(string installedExe) => $"\"{installedExe}\" --tray";

    public static bool CanEnable => File.Exists(SelfUpdate.InstalledExe);

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled && CanEnable) key.SetValue(ValueName, RunValue(SelfUpdate.InstalledExe));
            else
            {
                if (enabled) Log.Write("Chưa cài TDN Control nên không bật chạy cùng Windows.");
                key.DeleteValue(ValueName, false);
            }
        }
        catch (Exception ex) { Log.Write("Không đặt được chạy cùng Windows: " + ex.Message); }
    }
}

/// <summary>Cài TDN Control vào máy và tự cập nhật qua thư mục LAN (CAP_NHAT\TDN_Control).</summary>
public static class SelfUpdate
{
    public static string InstalledExe => Path.Combine(Store.InstallDir, AppInfo.SelfExeName);

    public static bool IsRunningFromInstallDir =>
        string.Equals(Path.GetFullPath(Environment.ProcessPath ?? ""), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

    /// <summary>Chép bản đang chạy vào %LocalAppData%\Programs\TDN Control, tạo lối tắt, mở bản đã cài.</summary>
    /// <param name="beforeLaunch">Gọi ngay trước khi mở bản đã cài (Program dùng để nhả mutex đúng lúc).</param>
    public static void InstallSelf(Action? beforeLaunch = null)
    {
        Directory.CreateDirectory(Store.InstallDir);
        File.Copy(Environment.ProcessPath!, InstalledExe, true);
        Shortcut.TryCreateDesktop(AppInfo.Name, InstalledExe);
        try
        {
            string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppInfo.Name + ".lnk");
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType != null)
            {
                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic s = shell.CreateShortcut(startMenu);
                s.TargetPath = InstalledExe; s.WorkingDirectory = Store.InstallDir; s.Save();
            }
        }
        catch { }
        beforeLaunch?.Invoke();
        Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = true, WorkingDirectory = Store.InstallDir });
    }

    public static UpdateManifest? GetLatest(ControlSettings settings)
    {
        string manifest = Path.Combine(settings.UpdateFolder, AppInfo.SelfFolderName, "version.json");
        return File.Exists(manifest) ? Store.ReadJson<UpdateManifest>(manifest) : null;
    }

    public static bool HasUpdate(UpdateManifest? latest) =>
        latest != null && VersionUtil.Compare(latest.Version, AppInfo.Version) > 0;

    /// <summary>
    /// Script cập nhật CỐ ĐỊNH (chỉ ký tự ASCII, không nhúng đường dẫn). Tham số:
    /// %1 = PID cần chờ thoát, %2 = file mới (.new), %3 = exe đích, %4 = file log, %5... = tham số mở lại.
    /// Thay thất bại (exe bị khóa) thì thử lại; hết lượt vẫn mở exe cũ để app không biến mất.
    /// </summary>
    internal const string UpdateScript =
        "@echo off\r\n" +
        "setlocal EnableExtensions\r\n" +
        "set \"PID=%~1\"\r\n" +
        "set \"SRC=%~2\"\r\n" +
        "set \"DST=%~3\"\r\n" +
        "set \"LOG=%~4\"\r\n" +
        "set \"ARGS=\"\r\n" +
        ":coll\r\n" +
        "if \"%~5\"==\"\" goto begin\r\n" +
        "set \"ARGS=%ARGS% %~5\"\r\n" +
        "shift /5\r\n" +
        "goto coll\r\n" +
        ":begin\r\n" +
        "set /a N=0\r\n" +
        ":wait\r\n" +
        "\"%SystemRoot%\\System32\\tasklist.exe\" /FI \"PID eq %PID%\" /NH /FO CSV 2>nul | \"%SystemRoot%\\System32\\find.exe\" \",\"\"%PID%\"\",\" >nul\r\n" +
        "if errorlevel 1 goto domove\r\n" +
        "set /a N+=1\r\n" +
        "if %N% GEQ 120 goto domove\r\n" +
        "\"%SystemRoot%\\System32\\ping.exe\" -n 2 127.0.0.1 >nul\r\n" +
        "goto wait\r\n" +
        ":domove\r\n" +
        "set /a T=0\r\n" +
        ":mv\r\n" +
        "move /y \"%SRC%\" \"%DST%\" >nul 2>>\"%LOG%\"\r\n" +
        "if not errorlevel 1 goto ok\r\n" +
        "set /a T+=1\r\n" +
        "echo [%date% %time%] move failed, try %T% >>\"%LOG%\"\r\n" +
        "if %T% GEQ 10 goto fail\r\n" +
        "\"%SystemRoot%\\System32\\ping.exe\" -n 3 127.0.0.1 >nul\r\n" +
        "goto mv\r\n" +
        ":ok\r\n" +
        "echo [%date% %time%] updated, restarting >>\"%LOG%\"\r\n" +
        "goto launch\r\n" +
        ":fail\r\n" +
        "echo [%date% %time%] gave up, restarting old exe >>\"%LOG%\"\r\n" +
        ":launch\r\n" +
        "cd /d \"%~dp3\"\r\n" +
        "start \"\" \"%DST%\"%ARGS%\r\n" +
        "(goto) 2>nul & del \"%~f0\"\r\n";

    /// <summary>Tham số mở lại sau khi cập nhật: giữ --tray và --portable nếu phiên hiện tại có.</summary>
    public static List<string> RestartArgs(IEnumerable<string> args)
    {
        var result = new List<string>();
        foreach (string flag in new[] { "--tray", "--portable" })
            if (args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase))) result.Add(flag);
        return result;
    }

    /// <summary>Ghi script cố định vào scriptPath và chạy nó (tham số truyền qua ArgumentList, không ghép chuỗi lệnh).</summary>
    public static Process StartUpdateScript(string scriptPath, int pid, string stagedFile, string targetExe,
        IEnumerable<string> restartArgs, string logFile, bool visible = false)
    {
        string? logDir = Path.GetDirectoryName(logFile);
        if (!string.IsNullOrEmpty(logDir)) Directory.CreateDirectory(logDir);
        File.WriteAllText(scriptPath, UpdateScript, new UTF8Encoding(false));
        // Gọi cmd tường minh và LUÔN bọc từng tham số trong ngoặc kép (đường dẫn Windows không chứa "),
        // để ký tự & | ^ ( ) trong đường dẫn không dấu cách không bị cmd hiểu là lệnh.
        var parts = new List<string> { scriptPath, pid.ToString(), stagedFile, targetExe, logFile };
        parts.AddRange(restartArgs);
        if (parts.Any(p => p.Contains('"'))) throw new ArgumentException("Đường dẫn không hợp lệ (chứa dấu \").");
        string inner = string.Join(" ", parts.Select(p => "\"" + p + "\""));
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d /s /c \"" + inner + "\"")
        { UseShellExecute = false, CreateNoWindow = !visible };
        return Process.Start(psi) ?? throw new InvalidOperationException("Không chạy được script cập nhật.");
    }

    /// <summary>Chép bản mới về (LAN hoặc tải từ GitHub nếu latest.DownloadUrl có giá trị), kiểm tra
    /// SHA-256, rồi nhờ một script nhỏ thay exe sau khi app thoát và mở lại.</summary>
    public static async Task ApplyAsync(ControlSettings settings, UpdateManifest latest)
    {
        if (!SafeName.IsSafe(latest.File))
            throw new InvalidDataException("version.json không hợp lệ: tên file \"" + latest.File + "\" không được phép.");
        string exe = Environment.ProcessPath!;
        string staged = exe + ".new";

        if (!string.IsNullOrWhiteSpace(latest.DownloadUrl))
        {
            using var response = await Http.GetAsync(latest.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync();
            await using (var output = new FileStream(staged, FileMode.Create, FileAccess.Write))
                await input.CopyToAsync(output);
        }
        else
        {
            string source = Path.Combine(settings.UpdateFolder, AppInfo.SelfFolderName, latest.File);
            await Task.Run(() =>
            {
                if (!File.Exists(source)) throw new FileNotFoundException("Thư mục cập nhật chưa có " + latest.File);
                File.Copy(source, staged, true);
            });
        }

        if (!string.IsNullOrWhiteSpace(latest.Sha256))
        {
            string hash;
            await using (var stream = File.OpenRead(staged))
                hash = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream)).ToLowerInvariant();
            if (!hash.Equals(latest.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(staged);
                throw new InvalidDataException("File cập nhật TDN Control bị lỗi (sai mã SHA-256).");
            }
        }
        int pid = Environment.ProcessId;
        string script = Path.Combine(Path.GetTempPath(), $"tdn-control-update-{pid}.cmd");
        string log = Path.Combine(Store.DataDir, "logs", "self-update.log");
        StartUpdateScript(script, pid, staged, exe, RestartArgs(Environment.GetCommandLineArgs().Skip(1)), log);
        Log.Write($"Tự cập nhật TDN Control {AppInfo.Version} -> {latest.Version}" +
            (latest.DownloadUrl != null ? " (qua GitHub)" : " (qua LAN)"));
    }

    private static HttpClient Http => GitHubReleases.Http;

    /// <summary>Đọc bản TDN Control mới nhất từ GitHub Releases công khai — DỰ PHÒNG khi không đọc được
    /// thư mục LAN (ví dụ máy không còn trong mạng nội bộ).</summary>
    public static async Task<UpdateManifest?> GetLatestFromGitHubAsync(CancellationToken cancellationToken = default)
    {
        var latest = await GitHubReleases.GetLatestAsync("vohaidang98/TDN_Control",
            name => string.Equals(name, AppInfo.SelfExeName, StringComparison.OrdinalIgnoreCase), cancellationToken: cancellationToken);
        if (latest != null) latest.File = AppInfo.SelfExeName;
        return latest;
    }
}

/// <summary>
/// Đọc bản mới nhất của một repo trên GitHub Releases công khai (dùng chung cho TDN Control và các app
/// đã chuyển sang phát hành qua GitHub). Kết quả được nhớ vài phút cho mỗi repo để không vượt giới hạn
/// 60 lượt/giờ của GitHub khi chưa đăng nhập (bảng điều khiển quét lại mỗi phút).
/// </summary>
public static class GitHubReleases
{
    internal static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TDNControl-Updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>Thời gian nhớ kết quả của một repo.</summary>
    public static readonly TimeSpan CacheTime = TimeSpan.FromMinutes(10);
    /// <summary>Bấm "Kiểm tra lại" chỉ hỏi lại GitHub khi kết quả cũ hơn mức này.</summary>
    private static readonly TimeSpan MinRefresh = TimeSpan.FromSeconds(30);

    private sealed record CacheItem(UpdateManifest? Manifest, DateTime FetchedUtc);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, CacheItem> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Bản mới nhất của repo ("chủ/tên"). isAsset chọn file cài (asset đầu tiên khớp); nếu có asset
    /// "&lt;tên&gt;.sha256" thì lấy mã SHA-256 từ đó. Lỗi mạng/không có bản phát hành: trả về bản đọc được
    /// lần trước (nếu có) hoặc null — không bao giờ ném lỗi.
    /// </summary>
    public static async Task<UpdateManifest?> GetLatestAsync(string repo, Func<string, bool> isAsset,
        bool refresh = false, CancellationToken cancellationToken = default)
    {
        Cache.TryGetValue(repo, out var cached);
        if (cached != null)
        {
            var age = DateTime.UtcNow - cached.FetchedUtc;
            if (age < (refresh ? MinRefresh : CacheTime)) return Clone(cached.Manifest);
        }
        var fresh = await FetchAsync(repo, isAsset, cancellationToken);
        // Hỏi lỗi (mạng, hết lượt API...) thì giữ bản đọc được lần trước, chỉ ghi nhận thời điểm để không hỏi dồn.
        var keep = fresh ?? cached?.Manifest;
        Cache[repo] = new CacheItem(keep, DateTime.UtcNow);
        return Clone(keep);
    }

    private static UpdateManifest? Clone(UpdateManifest? m) => m == null ? null : new UpdateManifest
    { Version = m.Version, File = m.File, Sha256 = m.Sha256, Notes = m.Notes, DownloadUrl = m.DownloadUrl };

    private static async Task<UpdateManifest?> FetchAsync(string repo, Func<string, bool> isAsset, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Http.GetAsync(
                "https://api.github.com/repos/" + repo + "/releases/latest", cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = doc.RootElement;
            string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            string version = tag.TrimStart('v', 'V');
            if (string.IsNullOrEmpty(version) || !root.TryGetProperty("assets", out var assets) || assets.ValueKind != System.Text.Json.JsonValueKind.Array)
                return null;

            var all = new List<(string Name, string Url)>();
            foreach (var asset in assets.EnumerateArray())
            {
                string name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                string url = asset.TryGetProperty("browser_download_url", out var du) ? du.GetString() ?? "" : "";
                if (name.Length > 0 && url.Length > 0) all.Add((name, url));
            }
            var file = all.FirstOrDefault(a => isAsset(a.Name));
            if (string.IsNullOrEmpty(file.Url)) return null;
            string? sha256Url = all.FirstOrDefault(a => string.Equals(a.Name, file.Name + ".sha256", StringComparison.OrdinalIgnoreCase)).Url;

            string sha256 = "";
            if (!string.IsNullOrEmpty(sha256Url))
            {
                try
                {
                    string text = await Http.GetStringAsync(sha256Url, cancellationToken);
                    sha256 = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                }
                catch { }
            }
            string notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            return new UpdateManifest { Version = version, File = file.Name, Sha256 = sha256, Notes = notes, DownloadUrl = file.Url };
        }
        catch { return null; }
    }
}

/// <summary>Đọc trạng thái Render LAN của Render Music Pro (thư mục share trong cài đặt của Render Music Pro).</summary>
public static class RenderLanStatus
{
    public sealed record Machine(string Name, bool Accept, string? CurrentJob, string Version, DateTime LastSeenUtc);
    public sealed record Snapshot(string? ShareRoot, string? Error, List<Machine> Machines, int Pending, int Running, int Failed, int WaitingCollect);

    // Render Music Pro >= 1.5.2: lan-render.json tách 3 thư mục rieng (MusicRoot/VideoRoot/OutRoot)
    // thay cho 1 "ShareRoot" chung; phieu viec + trang thai may (_RenderLAN, out) nam duoi OutRoot.
    // Van giu doc ShareRoot cu de tuong thich voi ban rat cu chua tung mo lai sau khi nang cap.
    private sealed class LanSettingsFile
    {
        public string ShareRoot { get; set; } = "";
        public string OutRoot { get; set; } = "";
    }
    private sealed class MachineFile
    {
        public string Machine { get; set; } = "";
        public bool Accept { get; set; }
        public string? CurrentJob { get; set; }
        public string AppVersion { get; set; } = "";
    }

    public static Snapshot Read()
    {
        string settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RenderMusicPro", "lan-render.json");
        var lan = File.Exists(settings) ? Store.ReadJson<LanSettingsFile>(settings) : null;
        string? root = lan?.OutRoot?.Trim();
        if (string.IsNullOrEmpty(root)) root = lan?.ShareRoot?.Trim();
        if (string.IsNullOrEmpty(root))
            return new Snapshot(null, "Máy này chưa chọn thư mục share trong Render Music Pro (nút RENDER LAN).", new(), 0, 0, 0, 0);
        try
        {
            string baseDir = Path.Combine(root, "_RenderLAN");
            var machines = new List<Machine>();
            string machinesDir = Path.Combine(baseDir, "machines");
            if (Directory.Exists(machinesDir))
            {
                foreach (string file in Directory.GetFiles(machinesDir, "*.json"))
                {
                    var seen = File.GetLastWriteTimeUtc(file);
                    if (DateTime.UtcNow - seen > TimeSpan.FromMinutes(10)) continue;
                    var m = Store.ReadJson<MachineFile>(file);
                    if (m != null) machines.Add(new Machine(m.Machine, m.Accept, m.CurrentJob, m.AppVersion, seen));
                }
            }
            int Count(string sub, string pattern)
            {
                string dir = Path.Combine(baseDir, "jobs", sub);
                return Directory.Exists(dir) ? Directory.GetFiles(dir, pattern).Length : 0;
            }
            string outDir = Path.Combine(root, "out");
            int waiting = Directory.Exists(outDir)
                ? Directory.GetDirectories(outDir).Count(d => File.Exists(Path.Combine(d, "done.json"))) : 0;
            return new Snapshot(root, null, machines.OrderBy(m => m.Name).ToList(),
                Count("pending", "*.json"), Count("running", "*.json"), Count("failed", "*.json"), waiting);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new Snapshot(root, "Không truy cập được thư mục share: " + ex.Message, new(), 0, 0, 0, 0);
        }
    }
}
