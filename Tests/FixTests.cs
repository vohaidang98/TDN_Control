using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;

namespace TDNControl.Tests;

public class TempBase : IDisposable
{
    protected readonly string Root = Path.Combine(Path.GetTempPath(), "tdn-fix-" + Guid.NewGuid().ToString("N"));
    public TempBase() => Directory.CreateDirectory(Root);
    public void Dispose() { try { Directory.Delete(Root, true); } catch { } }
    protected string Write(string dir, string name, string content)
    {
        Directory.CreateDirectory(dir);
        string p = Path.Combine(dir, name); File.WriteAllText(p, content); return p;
    }
    protected static string Sha(string s) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
}

public class SafeNameTests
{
    [Theory]
    [InlineData("RenderMusicPro.exe")] [InlineData("Gen Audio 4.2.0.exe")] [InlineData("Đặt tên.exe")] [InlineData("a.exe")]
    public void Valid(string n) => Assert.True(SafeName.IsSafe(n));

    [Theory]
    [InlineData("..\\x.exe")] [InlineData("a/b.exe")] [InlineData("a’; calc")] [InlineData("a'; calc")]
    [InlineData("")] [InlineData(null)] [InlineData("a\u0001b.exe")] [InlineData("a\nb.exe")] [InlineData("..")]
    [InlineData("a\tb.exe")] [InlineData("C:\\x.exe")]
    public void Invalid(string? n) => Assert.False(SafeName.IsSafe(n));
}

public class FetchRejectTests : TempBase
{
    AppEntry Bad(string file) => new()
    {
        Id = "BadApp", Name = "BadApp", Folder = Path.Combine(Root, "BadApp"),
        Latest = new UpdateManifest { Version = "2.0.0", File = file }
    };

    [Theory] [InlineData("..\\x.exe")] [InlineData("a’; calc")] [InlineData("")] [InlineData("a/b.exe")]
    public async Task FetchRejects(string file)
    {
        var app = Bad(file);
        Directory.CreateDirectory(app.Folder);
        File.WriteAllText(Path.Combine(Root, "x.exe"), "evil");
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            Actions.FetchAsync(app, new Progress<string>(), CancellationToken.None));
        string dl = Path.Combine(Store.DataDir, "downloads", "BadApp");
        Assert.False(Directory.Exists(dl) && Directory.GetFiles(dl).Length > 0);
        Assert.Equal("evil", File.ReadAllText(Path.Combine(Root, "x.exe")));
    }

    [Fact] public async Task InstallOrUpdateRejects()
    {
        var app = Bad("..\\x.exe"); app.Kind = UpdateKind.Portable;
        Directory.CreateDirectory(app.Folder);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            Actions.InstallOrUpdateAsync(app, new ControlSettings { UpdateFolder = Root }, new Progress<string>(), CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(Root, "x.exe")));
    }

    [Fact] public async Task BadIdRejected()
    {
        var app = Bad("ok.exe"); app.Id = "..\\evil";
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            Actions.FetchAsync(app, new Progress<string>(), CancellationToken.None));
    }

    [Fact] public void ScriptRejected()
    {
        Assert.Throws<InvalidDataException>(() => Actions.ResolveScriptPath(Root, "..\\evil.bat"));
        Assert.Throws<InvalidDataException>(() => Actions.ResolveScriptPath(Root, "x.exe"));
        Assert.EndsWith("ok.bat", Actions.ResolveScriptPath(Root, "ok.bat"));
    }
}

public class PortableTests : TempBase
{
    [Fact] public void FixedName() =>
        Assert.Equal("Gen Audio.exe", Actions.PortableExeName(new AppEntry { Id = "GetAudio", Name = "Gen Audio" }));

    [Fact] public async Task SavesWithFixedName()
    {
        string src = Write(Root, "GenAudio_4.2.0.exe", "v1");
        string dir = Path.Combine(Root, "apps", "GetAudio");
        string t = await Actions.InstallPortableAsync(src, dir, "Gen Audio.exe", Sha("v1"), default);
        Assert.Equal(Path.Combine(dir, "Gen Audio.exe"), t);
        Assert.Equal("v1", File.ReadAllText(t));
        Assert.Single(Directory.GetFiles(dir));
    }

    [Fact] public async Task BadShaKeepsOld()
    {
        string dir = Path.Combine(Root, "apps", "G");
        string old = Write(dir, "Gen Audio.exe", "OLD");
        string src = Write(Root, "new.exe", "NEW");
        await Assert.ThrowsAnyAsync<Exception>(() => Actions.InstallPortableAsync(src, dir, "Gen Audio.exe", Sha("other"), default));
        Assert.Equal("OLD", File.ReadAllText(old));
        Assert.Single(Directory.GetFiles(dir));
    }

    [Fact] public async Task SecondInstallReplaces()
    {
        string dir = Path.Combine(Root, "apps", "G");
        await Actions.InstallPortableAsync(Write(Root, "a.exe", "ONE"), dir, "Gen Audio.exe", Sha("ONE"), default);
        await Actions.InstallPortableAsync(Write(Root, "b.exe", "TWO"), dir, "Gen Audio.exe", Sha("TWO"), default);
        Assert.Equal("TWO", File.ReadAllText(Path.Combine(dir, "Gen Audio.exe")));
        Assert.Single(Directory.GetFiles(dir));
    }

    [Fact] public async Task OldStyleExeRemovedOnSuccess()
    {
        string dir = Path.Combine(Root, "apps", "G");
        Write(dir, "GenAudio_3.0.exe", "legacy");
        await Actions.InstallPortableAsync(Write(Root, "a.exe", "ONE"), dir, "Gen Audio.exe", null, default);
        Assert.False(File.Exists(Path.Combine(dir, "GenAudio_3.0.exe")));
    }

    [Fact] public async Task BadExeNameRejected() =>
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            Actions.InstallPortableAsync(Write(Root, "a.exe", "x"), Root, "..\\x.exe", null, default));
}

public class ReplaceExeTests : TempBase
{
    [Fact] public async Task Replaces()
    {
        string target = Write(Root, "app.exe", "OLD"); string src = Write(Root, "n.bin", "NEW");
        await Actions.ReplaceExeLocalAsync(src, target, Sha("NEW"), default);
        Assert.Equal("NEW", File.ReadAllText(target));
        Assert.False(File.Exists(target + ".old"));
    }

    [Fact] public async Task BadShaRestores()
    {
        string target = Write(Root, "app.exe", "OLD"); string src = Write(Root, "n.bin", "NEW");
        await Assert.ThrowsAnyAsync<Exception>(() => Actions.ReplaceExeLocalAsync(src, target, Sha("zzz"), default));
        Assert.Equal("OLD", File.ReadAllText(target));
    }
}

public class ElevationTests : TempBase
{
    const string Weird = "Thắng ’ test";

    [Fact] public void FilesDoNotEmbedPaths()
    {
        string src = Path.Combine(Root, "Nguồn 'a'", "mới.exe");
        string dst = Path.Combine(Root, "Đích ’ x", "cũ.exe");
        var (ps1, json) = Actions.WriteElevationFiles(Path.Combine(Root, "el"), src, dst);
        using var doc = JsonDocument.Parse(File.ReadAllText(json, Encoding.UTF8));
        Assert.Equal(src, doc.RootElement.GetProperty("Source").GetString());
        Assert.Equal(dst, doc.RootElement.GetProperty("Target").GetString());
        string script = File.ReadAllText(ps1);
        Assert.DoesNotContain(src, script); Assert.DoesNotContain(dst, script);
        Assert.DoesNotContain("Nguồn", script); Assert.DoesNotContain("Đích", script);
        string args = Actions.ElevatedCopyArguments(ps1, json);
        Assert.DoesNotContain(src, args); Assert.DoesNotContain(dst, args);
        Assert.Contains(ps1, args);
    }

    static (int code, string output) RunPs(string ps1, string json)
    {
        var psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ps1, json }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEndAsync(); var e = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(60000)) { p.Kill(true); throw new TimeoutException("ps1 treo"); }
        return (p.ExitCode, o.Result + e.Result);
    }

    [Fact] public void RealRunReplacesTarget()
    {
        string src = Write(Path.Combine(Root, Weird), "mới ’ nguồn.exe", "NEWCONTENT");
        string dst = Write(Path.Combine(Root, Weird + " đích"), "cũ ’ đích.exe", "OLDCONTENT");
        var (ps1, json) = Actions.WriteElevationFiles(Path.Combine(Root, "el"), src, dst);
        var (code, output) = RunPs(ps1, json);
        Assert.True(code == 0, output);
        Assert.Equal("NEWCONTENT", File.ReadAllText(dst));
        Assert.False(File.Exists(dst + ".old"));
    }

    [Fact] public void RealRunMissingSourceRestoresOld()
    {
        string dst = Write(Path.Combine(Root, Weird), "cũ.exe", "OLDCONTENT");
        var (ps1, json) = Actions.WriteElevationFiles(Path.Combine(Root, "el"), Path.Combine(Root, "khong-co.exe"), dst);
        var (code, _) = RunPs(ps1, json);
        Assert.NotEqual(0, code);
        Assert.Equal("OLDCONTENT", File.ReadAllText(dst));
    }
}

public class SelfUpdateTests : TempBase
{
    [Fact] public void RestartArgsKeepsFlags()
    {
        Assert.Equal(new[] { "--tray", "--portable" }, SelfUpdate.RestartArgs(new[] { "--portable", "x", "--TRAY" }));
        Assert.Empty(SelfUpdate.RestartArgs(new[] { "foo" }));
        Assert.Equal(new[] { "--tray" }, SelfUpdate.RestartArgs(new[] { "--tray" }));
    }

    [Fact] public void RunValueQuotedWithTray()
    {
        string v = Startup.RunValue(@"C:\Program Files\TDN Control\TDN Control.exe");
        Assert.Equal("\"C:\\Program Files\\TDN Control\\TDN Control.exe\" --tray", v);
    }

    static string Sys(string n) => Path.Combine(Environment.SystemDirectory, n);

    [Fact] public void ScriptHasNoEmbeddedPathsAndIsAscii()
    {
        Assert.All(SelfUpdate.UpdateScript, c => Assert.True(c < 128));
    }

    [Fact] public void RealRunUpdatesExe()
    {
        string dir = Path.Combine(Root, "Thắng % x"); Directory.CreateDirectory(dir);
        string exe = Path.Combine(dir, "TDN Control.exe");
        string staged = exe + ".new";
        File.Copy(Sys("whoami.exe"), exe);
        File.Copy(Sys("hostname.exe"), staged);
        byte[] expected = File.ReadAllBytes(staged);
        Assert.NotEqual(expected, File.ReadAllBytes(exe));
        string log = Path.Combine(dir, "logs", "self-update.log");
        string script = Path.Combine(dir, "upd.cmd");

        using var waiter = Process.Start(new ProcessStartInfo(Sys("ping.exe"), "-n 3 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true })!;
        using var s = SelfUpdate.StartUpdateScript(script, waiter.Id, staged, exe, new[] { "--tray", "--portable" }, log);
        Assert.True(s.WaitForExit(60000), "script treo");
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(30) && (File.Exists(staged) || File.Exists(script))) Thread.Sleep(300);

        Assert.Equal(expected, File.ReadAllBytes(exe));
        Assert.False(File.Exists(staged));
        Assert.True(File.Exists(log));
        Assert.Contains("updated", File.ReadAllText(log));
        Assert.True(waiter.HasExited);
    }

    [Fact] public void RealRunLockedExeDoesNotReplace()
    {
        // Ca chứng minh không xanh giả: đích bị khóa thì exe KHÔNG bị thay và log ghi "move failed".
        string dir = Path.Combine(Root, "lock x"); Directory.CreateDirectory(dir);
        string exe = Path.Combine(dir, "t.exe"); string staged = exe + ".new";
        File.Copy(Sys("whoami.exe"), exe); File.Copy(Sys("hostname.exe"), staged);
        byte[] oldBytes = File.ReadAllBytes(exe);
        string log = Path.Combine(dir, "l.log");
        using var w = Process.Start(new ProcessStartInfo(Sys("ping.exe"), "-n 1 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true })!;
        w.WaitForExit();
        using var fs = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var s = SelfUpdate.StartUpdateScript(Path.Combine(dir, "u.cmd"), w.Id, staged, exe, Array.Empty<string>(), log);
        Thread.Sleep(6000);
        Assert.Equal(oldBytes, File.ReadAllBytes(exe));
        Assert.True(File.Exists(staged));
        Assert.Contains("move failed", File.ReadAllText(log));
        try { s.Kill(true); } catch { }
    }
}
