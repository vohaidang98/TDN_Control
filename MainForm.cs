using System.Diagnostics;

namespace TDNControl;

/// <summary>Cửa sổ chính: danh sách app TDN Media, cập nhật, khay hệ thống, Render LAN.</summary>
public sealed partial class MainForm : Form
{
    private sealed record ScanResult(List<AppEntry>? Apps, bool FolderOk, string? Error, UpdateManifest? SelfLatest);

    private readonly ControlSettings _settings = Store.Load();
    private readonly bool _startHidden;
    private bool _allowVisible;
    private bool _exiting;
    private bool _trayHintShown;
    private readonly CancellationTokenSource _cts = new();

    private List<AppEntry> _apps = new();
    private readonly Dictionary<string, AppRow> _rows = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _busy = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _notified = new(StringComparer.OrdinalIgnoreCase);

    private bool _scanning, _rescanPending, _manualPending, _refreshingRunning, _readingLan;
    private bool _installing; // một cờ chung: chỉ một lần cài/cập nhật chạy tại một thời điểm
    private UpdateManifest? _selfLatest;
    private string _lastScan = "chưa quét";
    private string? _lastError;

    // Giao diện
    private readonly FlowLayoutPanel _list = new();
    private readonly Panel _emptyPanel = new();
    private readonly Label _emptyLabel = UiKit.MakeLabel("", UiKit.Muted, 10.5f);
    private readonly Panel _banner = new();
    private readonly Label _bannerLabel = UiKit.MakeLabel("", UiKit.Gold, 10f, FontStyle.Bold);
    private readonly Button _bannerButton = UiKit.MakeButton("Cập nhật ngay", true, 130, 30);
    private readonly Button _btnRefresh = UiKit.MakeButton("⟳ Kiểm tra lại", false, 130);
    private readonly Button _btnUpdateAll = UiKit.MakeButton("⬆ Cập nhật tất cả", true, 150);
    private readonly Button _btnSettings = UiKit.MakeButton("⚙ Cài đặt", false, 100);
    private readonly Panel _renderCard = new SurfacePanel();
    private readonly Label _renderBody = UiKit.MakeLabel("Đang đọc...", UiKit.Muted);
    private readonly Label _footerStatus = UiKit.MakeLabel("", UiKit.Muted, 9f);

    private readonly NotifyIcon _tray = new();
    private readonly ContextMenuStrip _trayMenu = new();
    private readonly ToolStripMenuItem _trayApps = new("Mở app");

    private readonly System.Windows.Forms.Timer _scanTimer = new() { Interval = 60_000 };
    private readonly System.Windows.Forms.Timer _runTimer = new() { Interval = 3_000 };
    private readonly System.Windows.Forms.Timer _lanTimer = new() { Interval = 10_000 };

    public MainForm(bool startHidden)
    {
        _startHidden = startHidden;
        _allowVisible = !startHidden;

        Text = "TDN Control · TDN Media";
        Font = UiKit.Font();
        BackColor = UiKit.Bg; ForeColor = UiKit.Text;
        ClientSize = new Size(1280, 860);
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;
        MinimumSize = new Size(1080, 740);
        StartPosition = FormStartPosition.CenterScreen;
        var appIcon = UiKit.LoadIcon("app.ico");
        if (appIcon != null) Icon = appIcon;

        BuildUi();
        BuildTray(appIcon);

        _scanTimer.Tick += (_, _) => { if (_busy.Count == 0) _ = ScanAsync(); };
        _runTimer.Tick += async (_, _) => await RefreshRunningAsync();
        _lanTimer.Tick += async (_, _) => await ReadLanAsync();
        _scanTimer.Start(); _runTimer.Start(); _lanTimer.Start();

        // Luôn tạo handle ngay để luồng nghe gọi BeginInvoke(ShowFromTray) được (kể cả khi mở 2 lần liền).
        CreateHandle();
    }

    // ---------- Khởi động ẩn không nhấp nháy ----------
    protected override void SetVisibleCore(bool value)
    {
        if (!_allowVisible) value = false;
        base.SetVisibleCore(value);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UiKit.DarkTitleBar(Handle);
        BeginInvoke(new Action(async () =>
        {
            await ScanAsync();
            await ReadLanAsync();
        }));
    }

    public void ShowFromTray()
    {
        if (IsDisposed) return;
        _allowVisible = true;
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        TopMost = true; TopMost = false;
        BringToFront();
        _ = ScanAsync();
    }

    // ---------- Dựng giao diện ----------
    private void BuildUi()
    {
        BuildDashboard();
    }

    private void FitRows()
    {
        if (_fittingRows) return;
        _fittingRows = true;
        try
        {
            int w = Math.Max(300, _list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
            foreach (var row in _rows.Values)
                if (row.Width != w) row.Width = w;
        }
        finally { _fittingRows = false; }
    }

    // ---------- Khay hệ thống ----------
    private void BuildTray(Icon? icon)
    {
        _tray.Icon = icon ?? SystemIcons.Application;
        _tray.Text = "TDN Control";
        _trayMenu.BackColor = UiKit.Card; _trayMenu.ForeColor = UiKit.Text; _trayMenu.Font = UiKit.Font();
        var open = new ToolStripMenuItem("Mở TDN Control");
        open.Click += (_, _) => ShowFromTray();
        var check = new ToolStripMenuItem("Kiểm tra cập nhật");
        check.Click += async (_, _) => await ScanAsync(manual: true);
        var exit = new ToolStripMenuItem("Thoát");
        exit.Click += (_, _) => ExitApp(confirm: true);
        _trayMenu.Items.AddRange(new ToolStripItem[] { open, _trayApps, check, new ToolStripSeparator(), exit });
        _trayMenu.Opening += (_, _) => RebuildTrayApps();
        _tray.ContextMenuStrip = _trayMenu;
        _tray.DoubleClick += (_, _) => ShowFromTray();
        _tray.BalloonTipClicked += (_, _) => ShowFromTray();
        _tray.Visible = true;
    }

    private void RebuildTrayApps()
    {
        _trayApps.DropDownItems.Clear();
        foreach (var app in _apps.Where(a => a.IsInstalled || !string.IsNullOrWhiteSpace(a.LaunchCommand)))
        {
            var item = new ToolStripMenuItem(app.Name) { Tag = app };
            item.Click += (_, _) => LaunchApp(app);
            _trayApps.DropDownItems.Add(item);
        }
        if (_trayApps.DropDownItems.Count == 0)
            _trayApps.DropDownItems.Add(new ToolStripMenuItem("(chưa có app nào đã cài)") { Enabled = false });
    }

    private void ShowBalloon(string text)
    {
        try { _tray.ShowBalloonTip(6000, "TDN Control", text, ToolTipIcon.Info); } catch { }
    }

    private bool ConfirmExitWhileBusy() =>
        !_installing && _busy.Count == 0 ||
        Ask(this, "Đang cập nhật ứng dụng. Thoát bây giờ có thể làm hỏng bản cài. Vẫn thoát?");

    private void ExitApp(bool confirm = false)
    {
        if (confirm && !ConfirmExitWhileBusy()) return;
        _exiting = true;
        try { _cts.Cancel(); } catch { }
        DisposeTray();
        Application.Exit();
    }

    private void DisposeTray()
    {
        try { _tray.Visible = false; _tray.Dispose(); } catch { }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exiting && e.CloseReason == CloseReason.UserClosing && !_settings.CloseToTray && !ConfirmExitWhileBusy())
        {
            e.Cancel = true;
            return;
        }
        if (!_exiting && e.CloseReason == CloseReason.UserClosing && _settings.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            if (!_trayHintShown) { _trayHintShown = true; ShowBalloon("TDN Control vẫn chạy dưới khay"); }
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _scanTimer.Stop(); _runTimer.Stop(); _lanTimer.Stop();
        try { _cts.Cancel(); } catch { }
        DisposeTray();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _scanTimer.Dispose(); _runTimer.Dispose(); _lanTimer.Dispose();
            _trayMenu.Dispose(); _renderBody.Dispose(); _cts.Dispose();
        }
        base.Dispose(disposing);
    }

    // ---------- Quét ----------
    private static async Task<ScanResult> DoScanAsync(ControlSettings s, bool refreshGitHub)
    {
        bool ok = Directory.Exists(s.UpdateFolder);
        // App đã chuyển sang GitHub Releases vẫn được quét (và hiện bình thường) khi LAN không truy cập được;
        // app chỉ có LAN thì không có dòng nào, như trước.
        var apps = await Catalog.ScanAsync(s, ok, refreshGitHub);
        if (!ok) return new ScanResult(apps, false, "Không truy cập được " + s.UpdateFolder, null);
        UpdateManifest? self = null;
        try { self = SelfUpdate.GetLatest(s); } catch { }
        return new ScanResult(apps, true, null, self);
    }

    private async Task ScanAsync(bool manual = false)
    {
        if (_scanning)
        {
            _rescanPending = true;
            if (manual) _manualPending = true;
            return;
        }
        _scanning = true;
        _btnRefresh.Enabled = false;
        ScanResult res;
        try
        {
            res = await Task.Run(() => DoScanAsync(_settings, manual));
            // LAN không có bản TDN Control mới (hoặc chưa đọc được thư mục) -> thử GitHub Releases
            // công khai làm dự phòng, không thay thế LAN.
            if (res.SelfLatest == null)
            {
                UpdateManifest? github = await SelfUpdate.GetLatestFromGitHubAsync();
                if (github != null) res = res with { SelfLatest = github };
            }
        }
        catch (Exception ex)
        {
            Log.Write("Quét lỗi: " + ex);
            res = new ScanResult(null, true, "Quét lỗi: " + ex.Message, null);
        }
        _scanning = false;
        if (IsDisposed || _exiting) return;
        try { ApplyScan(res, manual); }
        catch (Exception ex) { Log.Write("Cập nhật giao diện lỗi: " + ex); }
        _btnRefresh.Enabled = true;
        if (_rescanPending)
        {
            bool m = _manualPending;
            _rescanPending = false; _manualPending = false;
            _ = ScanAsync(m);
        }
    }

    private void ApplyScan(ScanResult res, bool manual)
    {
        _lastScan = DateTime.Now.ToString("HH:mm:ss");
        _lastError = res.Error;
        if (res.Apps != null)
        {
            _apps = res.Apps;
            _selfLatest = res.SelfLatest;
            MergeRows(res.Apps);
        }
        UpdateEmptyState(res);
        ShowSelfBanner();
        UpdateHeaderState();
        UpdateFooter();
        NotifyNewVersions(manual);
    }

    private void MergeRows(List<AppEntry> apps)
    {
        _list.SuspendLayout();
        var ids = new HashSet<string>(apps.Select(a => a.Id), StringComparer.OrdinalIgnoreCase);
        foreach (string gone in _rows.Keys.Where(k => !ids.Contains(k)).ToList())
        {
            var r = _rows[gone];
            _list.Controls.Remove(r); r.Dispose(); _rows.Remove(gone);
        }
        int index = 0;
        foreach (var app in apps)
        {
            if (!_rows.TryGetValue(app.Id, out var row))
            {
                row = new AppRow(app) { UpdateLocked = _installing };
                row.OpenClicked += r => LaunchApp(r.Entry);
                row.CloseClicked += async r => await CloseAppAsync(r.Entry);
                row.UpdateClicked += async r => await UpdateSingleAsync(r.Entry);
                row.MoreClicked += (r, anchor) => ShowMoreMenu(r.Entry, anchor);
                _rows[app.Id] = row;
                _list.Controls.Add(row);
            }
            else if (!row.Busy) row.SetEntry(app);
            _list.Controls.SetChildIndex(row, index++);
        }
        FitRows();
        _list.ResumeLayout(true);
        ApplyAppFilter();
    }

    private void UpdateEmptyState(ScanResult res)
    {
        if (!res.FolderOk && (res.Apps == null || res.Apps.Count == 0))
        {
            _emptyLabel.Text = "Không truy cập được thư mục cập nhật:\n" + _settings.UpdateFolder +
                "\n\nKiểm tra mạng LAN hoặc đổi thư mục trong Cài đặt.";
            _emptyPanel.Visible = true; _emptyPanel.BringToFront();
        }
        else if (res.Apps != null && res.Apps.Count == 0)
        {
            _emptyLabel.Text = "Thư mục cập nhật chưa có ứng dụng nào\n(cần thư mục con có file version.json).";
            _emptyPanel.Visible = true; _emptyPanel.BringToFront();
        }
        else if (res.Apps != null) _emptyPanel.Visible = false;
    }

    private void ShowSelfBanner()
    {
        bool has = SelfUpdate.HasUpdate(_selfLatest);
        _banner.Visible = has;
        if (has) _bannerLabel.Text = $"Có TDN Control bản {_selfLatest!.Version} (đang dùng {AppInfo.Version})";
    }

    private void UpdateHeaderState()
    {
        _btnUpdateAll.Enabled = !_installing && _busy.Count == 0 && _apps.Any(a => a.HasUpdate);
        UpdateDashboardSummary();
    }

    /// <summary>Bắt đầu/kết thúc một lượt cài-cập nhật: khóa các nút Cập nhật/Cài đặt khác.</summary>
    private bool TryBeginInstall()
    {
        if (_installing) return false;
        _installing = true;
        foreach (var r in _rows.Values) r.UpdateLocked = true;
        UpdateHeaderState();
        return true;
    }

    private void EndInstall()
    {
        _installing = false;
        foreach (var r in _rows.Values) r.UpdateLocked = false;
        UpdateHeaderState();
    }

    private void UpdateFooter()
    {
        string text = "Quét lần cuối: " + _lastScan;
        if (!string.IsNullOrEmpty(_lastError)) text += " · Lỗi: " + _lastError;
        _footerStatus.Text = text;
        _footerStatus.ForeColor = string.IsNullOrEmpty(_lastError) ? UiKit.Muted : UiKit.Orange;
        bool viaGitHub = _apps.Any(a => a.Latest?.DownloadUrl != null);
        _sourceStatus.Text = string.IsNullOrEmpty(_lastError)
            ? (viaGitHub ? "Nguồn cập nhật LAN + GitHub" : "Nguồn cập nhật LAN")
            : (viaGitHub ? "LAN lỗi · app GitHub vẫn kiểm tra được" : "Kiểm tra kết nối LAN");
        _sourceStatus.ForeColor = string.IsNullOrEmpty(_lastError) ? UiKit.Green : UiKit.Orange;
        _sourcePath.Text = _settings.UpdateFolder;
    }

    private void NotifyNewVersions(bool manual)
    {
        var fresh = new List<string>();
        foreach (var app in _apps.Where(a => a.HasUpdate))
            if (_notified.Add(app.Id + "|" + app.Latest!.Version)) fresh.Add($"{app.Name} {app.Latest.Version}");
        if (SelfUpdate.HasUpdate(_selfLatest) && _notified.Add("SELF|" + _selfLatest!.Version))
            fresh.Add($"TDN Control {_selfLatest.Version}");
        if (fresh.Count > 0 && _settings.NotifyUpdates) ShowBalloon("Có bản mới: " + string.Join(", ", fresh));
        else if (manual && !Visible)
            ShowBalloon(_apps.Any(a => a.HasUpdate) ? "Có bản mới chưa cập nhật." : "Mọi ứng dụng đã là bản mới nhất.");
    }

    private async Task RefreshRunningAsync()
    {
        if (_refreshingRunning || _scanning || !Visible || IsDisposed) return;
        _refreshingRunning = true;
        try
        {
            var snap = _apps.ToList();
            await Task.Run(() => Catalog.RefreshRunning(snap));
            if (IsDisposed) return;
            foreach (var app in snap)
                if (_rows.TryGetValue(app.Id, out var row) && ReferenceEquals(row.Entry, app)) row.UpdateView();
            UpdateDashboardSummary();
            ApplyAppFilter();
        }
        catch (Exception ex) { Log.Write("Cập nhật trạng thái chạy lỗi: " + ex.Message); }
        finally { _refreshingRunning = false; }
    }

    // ---------- Render LAN ----------
    private async Task ReadLanAsync()
    {
        if (_readingLan || IsDisposed || (!Visible && _renderBody.Text != "Đang đọc...")) return;
        _readingLan = true;
        try
        {
            var snap = await Task.Run(RenderLanStatus.Read);
            if (IsDisposed) return;
            var lines = new List<string>();
            if (snap.ShareRoot != null) lines.Add("Thư mục share: " + snap.ShareRoot);
            if (snap.Error != null) lines.Add(snap.Error);
            foreach (var m in snap.Machines)
            {
                double age = (DateTime.UtcNow - m.LastSeenUtc).TotalSeconds;
                string state = age > 45 ? "mất kết nối"
                    : !m.Accept ? "không nhận bài"
                    : !string.IsNullOrWhiteSpace(m.CurrentJob) ? "đang render " + m.CurrentJob
                    : "rảnh, nhận bài";
                string ago = age < 60 ? $"{Math.Max(0, (int)age)} giây trước" : $"{(int)(age / 60)} phút trước";
                lines.Add($"{m.Name} · {state} · {ago}");
            }
            if (snap.Error == null)
            {
                if (snap.Machines.Count == 0) lines.Add("Chưa thấy máy nào báo trạng thái.");
                lines.Add($"Phiếu chờ: {snap.Pending} · Đang làm: {snap.Running} · Lỗi: {snap.Failed} · Kết quả chờ chuyển: {snap.WaitingCollect}");
            }
            _renderBody.Text = string.Join("\n", lines);
            _renderBody.ForeColor = snap.Error != null ? UiKit.Orange : UiKit.Text;
            _renderBody.AccessibleDescription = _renderBody.Text;
        }
        catch (Exception ex) { Log.Write("Đọc Render LAN lỗi: " + ex.Message); }
        finally { _readingLan = false; }
    }

    // ---------- Thao tác từng app ----------
    private static bool Ask(IWin32Window owner, string text, string title = "TDN Control") =>
        MessageBox.Show(owner, text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    private void LaunchApp(AppEntry app)
    {
        try
        {
            Actions.Launch(app);
            _ = Task.Delay(2500).ContinueWith(_ => { if (!IsDisposed) BeginInvoke(new Action(async () => await RefreshRunningAsync())); });
        }
        catch (Exception ex)
        {
            Log.Write($"Mở {app.Name} lỗi: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Không mở được " + app.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SetBusy(AppEntry app, bool busy, string? text = null)
    {
        if (busy) _busy.Add(app.Id); else _busy.Remove(app.Id);
        if (_rows.TryGetValue(app.Id, out var row)) row.SetBusy(busy, text);
        UpdateHeaderState();
    }

    private void SetProgress(AppEntry app, string text)
    {
        if (_rows.TryGetValue(app.Id, out var row)) row.SetProgress(text);
    }

    private void SetNote(AppEntry app, string text, Color color)
    {
        if (_rows.TryGetValue(app.Id, out var row)) row.SetNote(text, color);
    }

    private async Task CloseAppAsync(AppEntry app)
    {
        if (_busy.Contains(app.Id)) return;
        SetBusy(app, true, "Đang đóng...");
        try
        {
            bool closed = await Actions.CloseAsync(app, false);
            if (!closed && Ask(this, $"{app.Name} chưa tắt. Buộc tắt?"))
                closed = await Actions.CloseAsync(app, true);
            if (!closed) SetNote(app, "Chưa đóng được", UiKit.Orange);
        }
        catch (Exception ex)
        {
            Log.Write($"Đóng {app.Name} lỗi: {ex.Message}");
            SetNote(app, "Lỗi: " + ex.Message, UiKit.Red);
        }
        finally { SetBusy(app, false); }
        await RefreshRunningAsync();
    }

    private sealed record UpdateOutcome(bool Ok, bool Cancelled, string Message);

    /// <summary>Đóng app (nếu đang chạy) rồi cài/cập nhật. Không quét lại — người gọi quét.</summary>
    private async Task<UpdateOutcome> RunUpdateAsync(AppEntry app, bool askClose, bool quiet)
    {
        SetBusy(app, true, "Đang chuẩn bị...");
        try
        {
            while (_scanning) await Task.Delay(100);
            // Đọc lại trạng thái chạy ngay lúc này (giá trị Running có thể đã cũ tới 3 giây hoặc dừng khi cửa sổ ẩn).
            app.Running = await Task.Run(() => Catalog.IsRunning(app));
            if (app.Running)
            {
                if (askClose && !Ask(this, $"Đóng {app.Name} để cập nhật?"))
                    return new UpdateOutcome(false, true, "Đã hủy");
                SetProgress(app, "Đang đóng " + app.Name + "...");
                bool closed = await Actions.CloseAsync(app, false);
                if (!closed)
                {
                    if (!Ask(this, $"{app.Name} chưa tắt. Buộc tắt?")) return new UpdateOutcome(false, true, "Đã hủy");
                    closed = await Actions.CloseAsync(app, true);
                    if (!closed) throw new InvalidOperationException("Không tắt được " + app.Name);
                }
            }
            var progress = new Progress<string>(msg => SetProgress(app, msg));
            await Actions.InstallOrUpdateAsync(app, _settings, progress, _cts.Token);
            return new UpdateOutcome(true, false, "");
        }
        catch (OperationCanceledException ex)
        {
            Log.Write($"Hủy cập nhật {app.Name}: {ex.Message}");
            SetNote(app, ex.Message.Length > 0 ? ex.Message : "Đã hủy", UiKit.Orange);
            return new UpdateOutcome(false, true, ex.Message);
        }
        catch (Exception ex)
        {
            Log.Write($"Cập nhật {app.Name} lỗi: {ex}");
            SetNote(app, "Lỗi: " + ex.Message, UiKit.Red);
            if (!quiet && !IsDisposed)
                MessageBox.Show(this, ex.Message, "Lỗi cập nhật " + app.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return new UpdateOutcome(false, false, ex.Message);
        }
        finally { SetBusy(app, false); }
    }

    private async Task UpdateSingleAsync(AppEntry app)
    {
        if (_busy.Contains(app.Id)) return;
        if (!TryBeginInstall())
        {
            MessageBox.Show(this, "Đang có một ứng dụng khác được cài/cập nhật. Hãy đợi xong rồi thử lại.", "TDN Control",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        UpdateOutcome result;
        try { result = await RunUpdateAsync(app, askClose: true, quiet: false); }
        finally { if (!IsDisposed) EndInstall(); }
        if (IsDisposed || _exiting) return;
        await ScanAsync();
        if (!result.Ok && _rows.TryGetValue(app.Id, out var row) && result.Message.Length > 0)
            row.SetNote(result.Cancelled ? result.Message : "Lỗi: " + result.Message,
                result.Cancelled ? UiKit.Orange : UiKit.Red);
    }

    private async Task UpdateAllAsync()
    {
        var targets = _apps.Where(a => a.HasUpdate && !_busy.Contains(a.Id)).ToList();
        if (targets.Count == 0) return;
        var running = targets.Where(a => a.Running).ToList();
        string msg = $"Cập nhật {targets.Count} ứng dụng:\n" +
            string.Join("\n", targets.Select(a => $"• {a.Name}: {a.InstalledVersion} → {a.Latest!.Version}"));
        if (running.Count > 0)
            msg += "\n\nĐang chạy, sẽ được đóng lại: " + string.Join(", ", running.Select(a => a.Name));
        msg += "\n\nTiếp tục?";
        if (!Ask(this, msg, "Cập nhật tất cả")) return;
        if (!TryBeginInstall())
        {
            MessageBox.Show(this, "Đang có một ứng dụng khác được cài/cập nhật. Hãy đợi xong rồi thử lại.", "TDN Control",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        foreach (var t in targets) SetBusy(t, true, "Đang chờ tới lượt...");
        int ok = 0;
        var fails = new List<string>();
        try
        {
            foreach (var t in targets)
            {
                if (_exiting) break;
                var r = await RunUpdateAsync(t, askClose: false, quiet: true);
                if (r.Ok) ok++; else fails.Add($"{t.Name}: {(r.Message.Length > 0 ? r.Message : "đã hủy")}");
            }
        }
        finally { if (!IsDisposed) EndInstall(); }
        if (IsDisposed || _exiting) return;
        await ScanAsync();
        string summary = $"Xong: {ok}/{targets.Count} ứng dụng cập nhật thành công.";
        if (fails.Count > 0) summary += "\n\nKhông thành công:\n• " + string.Join("\n• ", fails);
        MessageBox.Show(this, summary, "Cập nhật tất cả", MessageBoxButtons.OK,
            fails.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
    }

    // ---------- Menu "⋯" ----------
    private void ShowMoreMenu(AppEntry app, Control anchor)
    {
        var menu = new ContextMenuStrip { BackColor = UiKit.Card, ForeColor = UiKit.Text, Font = UiKit.Font() };
        menu.Closed += (_, _) => BeginInvoke(new Action(menu.Dispose));

        menu.Items.Add("Chọn file exe...", null, (_, _) => PickExe(app));
        if (_settings.ExeOverrides.ContainsKey(app.Id))
            menu.Items.Add("Bỏ đường dẫn chọn tay", null, async (_, _) =>
            {
                _settings.ExeOverrides.Remove(app.Id); Store.Save(_settings); await ScanAsync();
            });
        var openInstall = menu.Items.Add("Mở thư mục cài", null, (_, _) => RevealFile(app.InstalledExe));
        openInstall.Enabled = app.InstalledExe != null;
        var openUpdate = menu.Items.Add("Mở thư mục cập nhật", null, (_, _) => OpenFolder(app.Folder));
        openUpdate.Enabled = !string.IsNullOrEmpty(app.Folder);
        menu.Items.Add("Ghi chú bản mới", null, (_, _) =>
        {
            string? notes = app.Latest?.Notes;
            MessageBox.Show(this, string.IsNullOrWhiteSpace(notes) ? "Chưa có ghi chú cho bản này." : notes,
                $"{app.Name} {app.Latest?.Version}", MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Ẩn khỏi bảng điều khiển", null, async (_, _) =>
        {
            if (!_settings.HiddenApps.Contains(app.Id, StringComparer.OrdinalIgnoreCase)) _settings.HiddenApps.Add(app.Id);
            Store.Save(_settings);
            await ScanAsync();
        });
        menu.Show(anchor, new Point(0, anchor.Height));
    }

    private void PickExe(AppEntry app)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Chọn file chạy của " + app.Name,
            Filter = "Chương trình (*.exe)|*.exe|Tất cả|*.*",
            CheckFileExists = true,
        };
        if (app.InstalledExe != null) dlg.InitialDirectory = Path.GetDirectoryName(app.InstalledExe);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _settings.ExeOverrides[app.Id] = dlg.FileName;
        Store.Save(_settings);
        _ = ScanAsync();
    }

    private void RevealFile(string? path)
    {
        if (path == null) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Write("Mở thư mục lỗi: " + ex.Message); }
    }

    private void OpenFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Write("Mở thư mục lỗi: " + ex.Message); }
    }

    // ---------- Cài đặt / tự cập nhật ----------
    private void OpenSettings()
    {
        using var dlg = new SettingsForm(_settings);
        if (dlg.ShowDialog(this) == DialogResult.OK) _ = ScanAsync(manual: false);
    }

    private async Task SelfUpdateAsync()
    {
        var latest = _selfLatest;
        if (latest == null) return;
        if (_busy.Count > 0 && !Ask(this, "Đang có ứng dụng được cập nhật. Vẫn cập nhật TDN Control?")) return;
        _bannerButton.Enabled = false;
        _bannerLabel.Text = "Đang cập nhật TDN Control...";
        try
        {
            await SelfUpdate.ApplyAsync(_settings, latest);
            ExitApp(confirm: false);
        }
        catch (Exception ex)
        {
            Log.Write("Tự cập nhật lỗi: " + ex);
            _bannerButton.Enabled = true;
            ShowSelfBanner();
            MessageBox.Show(this, ex.Message, "Không cập nhật được TDN Control", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
