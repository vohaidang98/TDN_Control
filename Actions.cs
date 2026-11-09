using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace TDNControl;

public static class Actions
{
    public static void Launch(AppEntry app)
    {
        if (!string.IsNullOrWhiteSpace(app.LaunchCommand))
        {
            Process.Start(new ProcessStartInfo(app.LaunchCommand!) { UseShellExecute = true });
            return;
        }
        if (app.InstalledExe == null || !File.Exists(app.InstalledExe))
            throw new FileNotFoundException("Chưa tìm thấy file chạy của " + app.Name + ". Bấm ⋯ > Chọn file exe.");
        Process.Start(new ProcessStartInfo(app.InstalledExe)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(app.InstalledExe)!
        });
    }

    /// <summary>Đóng lịch sự (như bấm X). Trả về true nếu app đã tắt hẳn.</summary>
    public static async Task<bool> CloseAsync(AppEntry app, bool force)
    {
        var processes = Catalog.GetProcesses(app);
        try
        {
            foreach (var p in processes)
            {
                try
                {
                    if (force) p.Kill(true);
                    else if (p.MainWindowHandle != IntPtr.Zero) p.CloseMainWindow();
                }
                catch { }
            }
            for (int i = 0; i < 20; i++)
            {
                if (processes.All(p => { try { return p.HasExited; } catch { return true; } })) return true;
                await Task.Delay(400);
            }
            return false;
        }
        finally { foreach (var p in processes) p.Dispose(); }
    }

    internal static async Task<string> HashAsync(string file, CancellationToken token)
    {
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).ToLowerInvariant();
    }

    /// <summary>Chép file cập nhật từ thư mục LAN về máy và kiểm tra SHA-256.</summary>
    internal static async Task<string> FetchAsync(AppEntry app, IProgress<string> progress, CancellationToken token)
    {
        var latest = app.Latest ?? throw new InvalidOperationException("Không đọc được version.json của " + app.Name);
        if (!SafeName.IsSafe(latest.File))
            throw new InvalidDataException("version.json không hợp lệ: tên file \"" + latest.File + "\" không được phép.");
        if (!SafeName.IsSafe(app.Id))
            throw new InvalidDataException("Tên thư mục app \"" + app.Id + "\" không hợp lệ, không cài.");
        string fileName = latest.File;
        string source = Path.Combine(app.Folder, fileName);
        string dir = Path.Combine(Store.DataDir, "downloads", app.Id);
        // Đọc LAN và dọn thư mục tạm chạy nền để không treo giao diện.
        bool exists = await Task.Run(() =>
        {
            if (!File.Exists(source)) return false;
            if (Directory.Exists(dir)) try { Directory.Delete(dir, true); } catch { }
            Directory.CreateDirectory(dir);
            return true;
        }, token);
        if (!exists) throw new FileNotFoundException("Thư mục cập nhật chưa có file " + fileName);
        string target = Path.Combine(dir, fileName);
        progress.Report($"Đang chép {fileName} về máy...");
        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, true))
        await using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, true))
        {
            var buffer = new byte[1 << 20];
            long total = input.Length, done = 0; int read, lastPct = -1;
            while ((read = await input.ReadAsync(buffer, token)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), token);
                done += read;
                int pct = total == 0 ? 100 : (int)(done * 100 / total);
                if (pct != lastPct && pct % 5 == 0) { lastPct = pct; progress.Report($"Đang chép {fileName}... {pct}%"); }
            }
        }
        if (!string.IsNullOrWhiteSpace(latest.Sha256))
        {
            progress.Report("Đang kiểm tra mã SHA-256...");
            string hash = await HashAsync(target, token);
            if (!hash.Equals(latest.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(target); } catch { }
                throw new InvalidDataException("File cập nhật bị lỗi (sai mã SHA-256). Không cài.");
            }
        }
        return target;
    }

    private static async Task<int> RunAsync(string exe, string args, bool elevate, bool hidden = false)
    {
        var psi = new ProcessStartInfo(exe, args) { UseShellExecute = true };
        if (elevate) psi.Verb = "runas";
        if (hidden) psi.WindowStyle = ProcessWindowStyle.Hidden;
        return await RunAsync(psi);
    }

    private static async Task<int> RunAsync(ProcessStartInfo psi)
    {
        try
        {
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("Không chạy được " + Path.GetFileName(psi.FileName));
            await p.WaitForExitAsync();
            return p.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("Bạn đã không cấp quyền quản trị.");
        }
    }

    private static bool CanWrite(string folder)
    {
        try
        {
            string probe = Path.Combine(folder, ".tdn-write-" + Guid.NewGuid().ToString("N")[..6]);
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    // ---------- Thay exe ----------

    /// <summary>
    /// Thay exe đã cài bằng bản mới (chạy nền). Lỗi giữa chừng hoặc SHA-256 sai thì khôi phục bản cũ
    /// từ file .old. Thành công thì xóa .old.
    /// </summary>
    internal static Task ReplaceExeLocalAsync(string source, string target, string? sha256, CancellationToken token) =>
        Task.Run(async () =>
        {
            string backup = target + ".old";
            bool hadOld = File.Exists(target);
            if (hadOld) File.Copy(target, backup, true);
            try
            {
                File.Copy(source, target, true);
                if (!string.IsNullOrWhiteSpace(sha256) &&
                    !(await HashAsync(target, token)).Equals(sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Đã chép nhưng file trên máy không khớp bản mới. Đã khôi phục bản cũ, hãy thử lại.");
            }
            catch
            {
                if (hadOld) { try { File.Copy(backup, target, true); } catch (Exception ex) { Log.Write("Không khôi phục được bản cũ: " + ex.Message); } }
                throw;
            }
            try { File.Delete(backup); } catch { }
        }, token);

    /// <summary>Script PowerShell CỐ ĐỊNH (không chứa đường dẫn) chạy với quyền quản trị; đường dẫn đọc từ file JSON UTF-8.</summary>
    internal const string ElevatedCopyScript =
        "param([Parameter(Mandatory=$true)][string]$JsonPath)\r\n" +
        "$ErrorActionPreference = 'Stop'\r\n" +
        "$old = $null; $had = $false; $p = $null\r\n" +
        "try {\r\n" +
        "  $p = Get-Content -LiteralPath $JsonPath -Raw -Encoding UTF8 | ConvertFrom-Json\r\n" +
        "  $old = $p.Target + '.old'\r\n" +
        "  $had = Test-Path -LiteralPath $p.Target\r\n" +
        "  if ($had) { Copy-Item -LiteralPath $p.Target -Destination $old -Force }\r\n" +
        "  Copy-Item -LiteralPath $p.Source -Destination $p.Target -Force\r\n" +
        "  if ($had) { Remove-Item -LiteralPath $old -Force -ErrorAction SilentlyContinue }\r\n" +
        "  exit 0\r\n" +
        "} catch {\r\n" +
        "  if ($had -and $old -and (Test-Path -LiteralPath $old)) { try { Copy-Item -LiteralPath $old -Destination $p.Target -Force } catch {} }\r\n" +
        "  exit 1\r\n" +
        "}\r\n";

    /// <summary>Ghi script .ps1 cố định + file JSON tham số (UTF-8) vào thư mục dir. Trả về (ps1, json).</summary>
    internal static (string Ps1, string Json) WriteElevationFiles(string dir, string source, string target)
    {
        Directory.CreateDirectory(dir);
        string id = Guid.NewGuid().ToString("N")[..10];
        string ps1 = Path.Combine(dir, "copy-" + id + ".ps1");
        string json = Path.Combine(dir, "args-" + id + ".json");
        File.WriteAllText(ps1, ElevatedCopyScript, new UTF8Encoding(true)); // BOM để PowerShell 5 đọc đúng
        File.WriteAllText(json, System.Text.Json.JsonSerializer.Serialize(new { Source = source, Target = target }), new UTF8Encoding(false));
        return (ps1, json);
    }

    /// <summary>Dòng tham số chạy script quản trị (đường dẫn chỉ là tham số, không nằm trong lệnh).</summary>
    internal static string ElevatedCopyArguments(string ps1, string json) =>
        $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{ps1}\" \"{json}\"";

    // ---------- Portable ----------

    /// <summary>Tên exe cố định của app portable: "Tên app.exe" (bỏ ký tự không hợp lệ).</summary>
    public static string PortableExeName(AppEntry app)
    {
        var chars = (string.IsNullOrWhiteSpace(app.Name) ? app.Id : app.Name).Select(c =>
            Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray();
        string name = new string(chars).Trim().TrimEnd('.');
        return (name.Length == 0 ? "app" : name) + ".exe";
    }

    /// <summary>
    /// Chép bản portable vào dir\exeName: chép ra file tạm cùng thư mục, kiểm tra SHA-256, rồi mới thay (ghi đè).
    /// Lỗi/SHA sai thì giữ nguyên bản cũ. Thành công thì xóa các exe kiểu cũ (khác tên) trong thư mục. Trả về đường dẫn exe.
    /// </summary>
    internal static Task<string> InstallPortableAsync(string source, string dir, string exeName, string? sha256, CancellationToken token) =>
        Task.Run(async () =>
        {
            if (Path.GetFileName(exeName) != exeName || exeName.Length == 0 || exeName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException("Tên file chạy không hợp lệ: " + exeName);
            Directory.CreateDirectory(dir);
            string target = Path.Combine(dir, exeName);
            string temp = target + ".tmp-" + Guid.NewGuid().ToString("N")[..6];
            try
            {
                File.Copy(source, temp, true);
                if (!string.IsNullOrWhiteSpace(sha256) &&
                    !(await HashAsync(temp, token)).Equals(sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("File chép vào máy không khớp mã SHA-256. Giữ nguyên bản cũ.");
                File.Move(temp, target, true);
            }
            catch
            {
                try { File.Delete(temp); } catch { }
                throw;
            }
            foreach (string other in Directory.GetFiles(dir, "*.exe"))
                if (!string.Equals(Path.GetFileName(other), exeName, StringComparison.OrdinalIgnoreCase))
                    try { File.Delete(other); } catch { }
            return target;
        }, token);

    // ---------- Script ----------

    /// <summary>Đường dẫn đầy đủ của script cập nhật; chỉ nhận tên file .bat/.cmd thuần nằm trong thư mục app.</summary>
    internal static string ResolveScriptPath(string folder, string? script)
    {
        if (string.IsNullOrWhiteSpace(script)) throw new InvalidOperationException("Không có script cập nhật.");
        string ext = Path.GetExtension(script);
        if (!SafeName.IsSafe(script) || !(ext.Equals(".bat", StringComparison.OrdinalIgnoreCase) || ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("tdn-app.json không hợp lệ: script \"" + script + "\" không được phép (chỉ nhận tên file .bat/.cmd trong thư mục app).");
        return Path.Combine(folder, script);
    }

    /// <summary>Cài/cập nhật app. App phải đang tắt. Mọi thao tác file nặng chạy nền; có thể gọi từ luồng UI.</summary>
    public static async Task InstallOrUpdateAsync(AppEntry app, ControlSettings settings, IProgress<string> progress, CancellationToken token)
    {
        string? scriptPath = app.Kind == UpdateKind.Script ? ResolveScriptPath(app.Folder, app.Script) : null;
        string file = await FetchAsync(app, progress, token);
        string version = app.Latest!.Version;
        switch (app.Kind)
        {
            case UpdateKind.Installer:
            {
                // Bộ cài Inno Setup (khóa gỡ cài đặt có đuôi _is1) chạy im lặng; loại khác mở giao diện bộ cài.
                bool inno = app.IsInstalled && app.RegistryKey?.EndsWith("_is1", StringComparison.OrdinalIgnoreCase) == true;
                progress.Report(inno ? "Đang cài bản mới (chế độ im lặng)..." : "Đang mở bộ cài, hãy làm theo hướng dẫn trên màn hình...");
                int code = await RunAsync(file, inno ? "/SILENT /SUPPRESSMSGBOXES /NORESTART /SP-" : "", false);
                if (code != 0) throw new InvalidOperationException($"Bộ cài kết thúc với mã lỗi {code}.");
                break;
            }
            case UpdateKind.ReplaceExe:
            {
                string target = app.InstalledExe ?? throw new InvalidOperationException(
                    app.Name + " chưa được cài trên máy này. Hãy cài bằng bộ cài lần đầu (hoặc bấm ⋯ > Chọn file exe).");
                string dir = Path.GetDirectoryName(target)!;
                progress.Report("Đang thay file chương trình...");
                if (await Task.Run(() => CanWrite(dir), token))
                {
                    await ReplaceExeLocalAsync(file, target, app.Latest.Sha256, token);
                }
                else
                {
                    // Thư mục Program Files: nhờ Windows cấp quyền quản trị để chép (script cố định + file JSON tham số).
                    string tmpDir = Path.Combine(Store.DataDir, "elevate");
                    var (ps1, json) = await Task.Run(() => WriteElevationFiles(tmpDir, file, target), token);
                    try
                    {
                        int code = await RunAsync("powershell.exe", ElevatedCopyArguments(ps1, json), true, true);
                        if (code != 0) throw new InvalidOperationException("Không thay được file chương trình (mã " + code + ").");
                    }
                    finally { try { File.Delete(ps1); } catch { } try { File.Delete(json); } catch { } }
                    if (!string.IsNullOrWhiteSpace(app.Latest.Sha256) &&
                        !(await HashAsync(target, token)).Equals(app.Latest.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Đã chép nhưng file trên máy không khớp bản mới. Hãy thử lại.");
                }
                break;
            }
            case UpdateKind.Portable:
            {
                string dir = Path.Combine(Store.PortableAppsDir, app.Id);
                progress.Report("Đang chép chương trình vào máy...");
                string target = await InstallPortableAsync(file, dir, PortableExeName(app), app.Latest.Sha256, token);
                app.InstalledExe = target;
                // Lối tắt Desktop: tạo/cập nhật mỗi lần cài (trỏ vào tên cố định).
                await Task.Run(() => Shortcut.TryCreateDesktop(app.Name, target), token);
                break;
            }
            case UpdateKind.Script:
            {
                progress.Report("Đang chạy script cập nhật...");
                var psi = new ProcessStartInfo(scriptPath!) { UseShellExecute = false, CreateNoWindow = false };
                int code = await RunAsync(psi);
                if (code != 0) throw new InvalidOperationException($"Script cập nhật kết thúc với mã lỗi {code}.");
                break;
            }
        }
        settings.InstalledByControl[app.Id] = version;
        Store.Save(settings);
        try { await Task.Run(() => File.Delete(file), token); } catch { }
        progress.Report($"✓ {app.Name} đã lên bản {version}.");
        Log.Write($"Cập nhật {app.Name} -> {version}");
    }
}

/// <summary>Kiểm tra tên file/thư mục lấy từ version.json, tdn-app.json trước khi đưa vào đường dẫn hay lệnh.</summary>
public static class SafeName
{
    private static readonly System.Text.RegularExpressions.Regex Allowed =
        new(@"^[\p{L}\p{N}_()\-]+([ .][\p{L}\p{N}_()\-]+)*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// true nếu là tên file thuần: không rỗng, không có dấu phân cách đường dẫn, không "..", không ký tự điều khiển,
    /// chỉ gồm chữ (kể cả có dấu), số, khoảng trắng, "_", "-", "(", ")" và dấu chấm giữa tên.
    /// </summary>
    public static bool IsSafe(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200) return false;
        if (name.Contains("..") || name.Any(char.IsControl)) return false;
        if (Path.GetFileName(name) != name) return false;
        return Allowed.IsMatch(name);
    }
}

public static class Shortcut
{
    public static void TryCreateDesktop(string name, string target)
    {
        try
        {
            string link = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), name + ".lnk");
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(link);
            shortcut.TargetPath = target;
            shortcut.WorkingDirectory = Path.GetDirectoryName(target);
            shortcut.IconLocation = target + ",0";
            shortcut.Save();
        }
        catch (Exception ex) { Log.Write("Không tạo được lối tắt: " + ex.Message); }
    }
}
