using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace TDNControl.Tests;

public class VersionTests
{
    [Fact] public void FourPartEqualsThreePart() => Assert.Equal(0, VersionUtil.Compare("1.4.0.0", "1.4.0"));
    [Fact] public void VPrefixGreater() => Assert.True(VersionUtil.Compare("v1.5.0", "1.4.9") > 0);
    [Fact] public void NumericNotLexical() => Assert.True(VersionUtil.Compare("1.10.0", "1.9.0") > 0);
    [Fact] public void LowerIsNegative() => Assert.True(VersionUtil.Compare("1.4.9", "v1.5.0") < 0);
    [Theory] [InlineData("")] [InlineData("   ")] [InlineData(null)] [InlineData("abc")] [InlineData("không có số")]
    public void NoDigitsParsesNull(string? s) => Assert.Null(VersionUtil.Parse(s));
    [Fact] public void ShortTrimsRevision() => Assert.Equal("1.4.0", VersionUtil.Short("1.4.0.0"));
    [Fact] public void ShortKeepsRevision() => Assert.Equal("1.4.0.2", VersionUtil.Short("1.4.0.2"));
}

public class HasUpdateTests
{
    static AppEntry Make(string? exe, string? installed, string? latest) => new()
    {
        InstalledExe = exe, InstalledVersion = installed,
        Latest = latest == null ? null : new UpdateManifest { Version = latest, File = "x.exe" }
    };
    [Fact] public void NotInstalled_False() => Assert.False(Make(null, "1.0.0", "2.0.0").HasUpdate);
    [Fact] public void NoInstalledVersion_False() => Assert.False(Make("C:\\a.exe", null, "2.0.0").HasUpdate);
    [Fact] public void LanNewer_True() => Assert.True(Make("C:\\a.exe", "1.9.0", "1.10.0").HasUpdate);
    [Fact] public void Equal_False() => Assert.False(Make("C:\\a.exe", "1.4.0", "1.4.0.0").HasUpdate);
    [Fact] public void LanOlder_False() => Assert.False(Make("C:\\a.exe", "2.0.0", "1.0.0").HasUpdate);
    [Fact] public void NoLatest_False() => Assert.False(Make("C:\\a.exe", "1.0.0", null).HasUpdate);
}

public class CatalogTempTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "tdn-test-" + Guid.NewGuid().ToString("N"));
    public CatalogTempTests() => Directory.CreateDirectory(root);
    public void Dispose() { try { Directory.Delete(root, true); } catch { } }

    string App(string id, string? versionJson = "{\"version\":\"1.0.0\",\"file\":\"a.exe\",\"sha256\":\"\"}", string? appJson = null)
    {
        string d = Path.Combine(root, id); Directory.CreateDirectory(d);
        if (versionJson != null) File.WriteAllText(Path.Combine(d, "version.json"), versionJson);
        if (appJson != null) File.WriteAllText(Path.Combine(d, "tdn-app.json"), appJson);
        return d;
    }
    List<AppEntry> Scan() => Catalog.Scan(new ControlSettings { UpdateFolder = root });

    [Fact] public void BackupFolderSkipped() { App("_sao-luu"); App("ClipMerge"); Assert.DoesNotContain(Scan(), a => a.Id == "_sao-luu"); }
    [Fact] public void NoVersionJsonSkipped() { App("NoManifest", null); App("ClipMerge"); var r = Scan(); Assert.Single(r); Assert.Equal("ClipMerge", r[0].Id); }
    [Fact] public void HiddenByConfiguredList()
    {
        App("SomeHiddenApp"); App("ClipMerge");
        var r = Catalog.Scan(new ControlSettings { UpdateFolder = root, HiddenApps = new() { "SomeHiddenApp" } });
        Assert.DoesNotContain(r, a => a.Id == "SomeHiddenApp");
    }
    [Fact] public void ShownIfNotInHiddenList()
    {
        App("SomeHiddenApp");
        var r = Catalog.Scan(new ControlSettings { UpdateFolder = root, HiddenApps = new() });
        Assert.Contains(r, a => a.Id == "SomeHiddenApp");
    }
    [Fact] public void HiddenTrueInOverride() { App("Other", appJson: "{\"hidden\": true}"); Assert.Empty(Scan()); }
    [Fact] public void KindPortableOverride()
    {
        App("SomeSetup", versionJson: "{\"version\":\"1.0.0\",\"file\":\"SomeSetup.exe\"}", appJson: "{\"kind\":\"portable\"}");
        Assert.Equal(UpdateKind.Portable, Scan().Single().Kind);
    }
    [Fact] public void KindDefaultsToInstallerForSetupWithoutOverride()
    {
        App("SomeSetup", versionJson: "{\"version\":\"1.0.0\",\"file\":\"SomeSetup.exe\"}");
        Assert.Equal(UpdateKind.Installer, Scan().Single().Kind);
    }
    [Fact] public void KnownClipMergeName() { App("ClipMerge"); Assert.Equal("ClipMerge", Scan().Single().Name); }
    [Fact] public void KnownGetAudio()
    {
        App("GetAudio"); var a = Scan().Single();
        Assert.Equal("Gen Audio", a.Name); Assert.Equal(UpdateKind.Portable, a.Kind);
    }
    [Fact] public void BrokenVersionJson()
    {
        App("ClipMerge", versionJson: "{ not json");
        var a = Scan().Single();
        Assert.False(string.IsNullOrEmpty(a.LatestError)); Assert.Null(a.Latest);
    }
    [Fact] public void GoodVersionJsonNoError() { App("ClipMerge"); var a = Scan().Single(); Assert.Null(a.LatestError); Assert.NotNull(a.Latest); }
    [Fact] public void MissingRootReturnsEmpty() => Assert.Empty(Catalog.Scan(new ControlSettings { UpdateFolder = Path.Combine(root, "nope") }));
}

public class CatalogRealTests
{
    [Fact] public void RealCapNhat_SixApps()
    {
        const string real = @"D:\Code\CAP_NHAT";
        Assert.True(Directory.Exists(real), "Không có thư mục thật " + real);
        var r = Catalog.Scan(new ControlSettings { UpdateFolder = real });
        Assert.Equal(6, r.Count);
        var names = r.Select(a => a.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var expected = new[] { "ClipForge Studio", "ClipMerge", "Gen Audio", "Muse Landscape Studio", "Render Music Pro", "TD Browser" }
            .OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(expected, names);
        Assert.DoesNotContain(r, a => a.Id.StartsWith('_'));
    }
}

public class FetchTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "tdn-fetch-" + Guid.NewGuid().ToString("N"));
    readonly string id = "test-" + Guid.NewGuid().ToString("N")[..8];
    readonly byte[] data = Encoding.UTF8.GetBytes("noi dung thu nghiem TDN " + Guid.NewGuid());
    public FetchTests() { Directory.CreateDirectory(root); File.WriteAllBytes(Path.Combine(root, "a.exe"), data); }
    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch { }
        try { Directory.Delete(Path.Combine(Store.DataDir, "downloads", id), true); } catch { }
    }
    AppEntry Entry(string sha, string file = "a.exe") => new()
    {
        Id = id, Name = "T", Folder = root,
        Latest = new UpdateManifest { Version = "1.0.0", File = file, Sha256 = sha }
    };
    sealed class P : IProgress<string> { public void Report(string v) { } }

    [Fact] public async Task CorrectSha_ReturnsCopy()
    {
        string sha = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        string path = await Actions.FetchAsync(Entry(sha), new P(), CancellationToken.None);
        Assert.True(File.Exists(path));
        Assert.Equal(data, File.ReadAllBytes(path));
        Assert.NotEqual(Path.Combine(root, "a.exe"), path);
    }
    [Fact] public async Task CorrectShaUppercase_Works()
    {
        string sha = Convert.ToHexString(SHA256.HashData(data));
        string path = await Actions.FetchAsync(Entry(sha), new P(), CancellationToken.None);
        Assert.True(File.Exists(path));
    }
    [Fact] public async Task WrongSha_ThrowsAndDeletesTemp()
    {
        string wrong = new string('0', 64);
        await Assert.ThrowsAsync<InvalidDataException>(() => Actions.FetchAsync(Entry(wrong), new P(), CancellationToken.None));
        string target = Path.Combine(Store.DataDir, "downloads", id, "a.exe");
        Assert.False(File.Exists(target));
    }
    [Fact] public async Task MissingFile_Throws() =>
        await Assert.ThrowsAsync<FileNotFoundException>(() => Actions.FetchAsync(Entry("", "khong-co.exe"), new P(), CancellationToken.None));
    [Fact] public async Task NoLatest_Throws() =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => Actions.FetchAsync(new AppEntry { Id = id, Name = "T", Folder = root }, new P(), CancellationToken.None));
}
