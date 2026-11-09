namespace TDNControl;

/// <summary>Một hàng thẻ cho mỗi app trên bảng điều khiển.</summary>
internal sealed class AppRow : SurfacePanel
{
    public AppEntry Entry { get; private set; }
    public bool Busy { get; private set; }
    private bool _updateLocked;
    /// <summary>true: đang có app khác cài/cập nhật, tạm tắt nút Cập nhật/Cài đặt của hàng này.</summary>
    public bool UpdateLocked { get => _updateLocked; set { _updateLocked = value; UpdateView(); } }
    private string? _progress;
    private string? _note;
    private Color _noteColor = UiKit.Red;
    private string? _iconPath;

    private readonly PictureBox _icon = new() { Size = new Size(44, 44), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
    private readonly Label _name = UiKit.MakeLabel("", UiKit.Text, 11.5f, FontStyle.Bold);
    private readonly Label _status = UiKit.MakeLabel("", UiKit.Muted);
    private readonly Label _version = UiKit.MakeLabel("", UiKit.Text);
    private readonly Label _badge = UiKit.MakeLabel("", UiKit.Muted, 9f, FontStyle.Bold);
    private readonly Button _open = UiKit.MakeButton("Mở app", false, 84);
    private readonly Button _close = UiKit.MakeButton("Đóng", false, 84);
    private readonly Button _update = UiKit.MakeButton("Cập nhật", true, 112);
    private readonly Button _more = UiKit.MakeButton("⋯", false, 36);
    private readonly ToolTip _tip = new();
    private static Image? _defaultIcon;

    public event Action<AppRow>? OpenClicked;
    public event Action<AppRow>? CloseClicked;
    public event Action<AppRow>? UpdateClicked;
    public event Action<AppRow, Control>? MoreClicked;

    public AppRow(AppEntry entry)
    {
        Entry = entry;
        Height = 108;
        BackColor = UiKit.Card;
        Margin = new Padding(0, 0, 0, 12);
        DoubleBuffered = true;
        Controls.AddRange(new Control[] { _icon, _name, _status, _version, _badge, _open, _close, _update, _more });
        _open.Click += (_, _) => OpenClicked?.Invoke(this);
        _close.Click += (_, _) => CloseClicked?.Invoke(this);
        _update.Click += (_, _) => UpdateClicked?.Invoke(this);
        _more.Click += (_, _) => MoreClicked?.Invoke(this, _more);
        UpdateView();
        DoLayout();
    }

    public void SetEntry(AppEntry entry) { Entry = entry; UpdateView(); }

    public void SetBusy(bool busy, string? progress = null)
    {
        Busy = busy; _progress = busy ? progress : null;
        if (busy) _note = null;
        UpdateView();
    }

    public void SetProgress(string? text) { _progress = text; UpdateView(); }

    public void SetNote(string? text, Color? color = null)
    {
        _note = text; _noteColor = color ?? UiKit.Red; UpdateView();
    }

    public void UpdateView()
    {
        var e = Entry;
        _name.Text = e.Name;
        if (_progress != null) { _status.Text = _progress; _status.ForeColor = UiKit.Gold; }
        else if (_note != null) { _status.Text = _note; _status.ForeColor = _noteColor; }
        else if (!e.IsInstalled) { _status.Text = "Chưa cài trên máy này"; _status.ForeColor = UiKit.Orange; }
        else if (e.Running) { _status.Text = "● Đang chạy"; _status.ForeColor = UiKit.Green; }
        else { _status.Text = "○ Đang tắt"; _status.ForeColor = UiKit.Muted; }
        _tip.SetToolTip(_status, _status.Text);

        _version.Text = $"Máy: {e.InstalledVersion ?? "?"} · LAN: {e.Latest?.Version ?? "lỗi"}";
        if (!string.IsNullOrEmpty(e.LatestError)) { _badge.Text = e.LatestError; _badge.ForeColor = UiKit.Red; }
        else if (e.HasUpdate) { _badge.Text = "CÓ BẢN MỚI"; _badge.ForeColor = UiKit.Orange; }
        else if (e.IsInstalled && e.InstalledVersion != null && e.Latest != null) { _badge.Text = "Mới nhất"; _badge.ForeColor = UiKit.Green; }
        else { _badge.Text = ""; }
        _tip.SetToolTip(_badge, _badge.Text);

        _open.Enabled = !Busy && (e.IsInstalled || !string.IsNullOrWhiteSpace(e.LaunchCommand));
        _close.Enabled = !Busy && e.Running;
        bool canInstall = !e.IsInstalled && e.Latest != null;
        _update.Visible = e.HasUpdate || canInstall;
        _update.Text = e.HasUpdate ? "Cập nhật" : "Cài đặt";
        _update.Enabled = !Busy && !_updateLocked;
        _more.Enabled = !Busy;
        UpdateIcon();
        DoLayout();
    }

    private void UpdateIcon()
    {
        string key = Entry.InstalledExe ?? "";
        if (key == _iconPath && _icon.Image != null) return;
        _iconPath = key;
        Image? img = null;
        if (key.Length > 0)
        {
            try
            {
                using var ico = Icon.ExtractAssociatedIcon(key);
                if (ico != null) img = UiKit.Resize(ico.ToBitmap(), 44);
            }
            catch { }
        }
        _defaultIcon ??= UiKit.Resize(UiKit.LoadImage("logo-d.png"), 44);
        _icon.Image = img ?? _defaultIcon;
    }

    protected override void OnResize(EventArgs eventargs) { base.OnResize(eventargs); DoLayout(); }

    private void DoLayout()
    {
        if (_more == null) return;
        int P(int n)=>(int)Math.Round(n*DeviceDpi/96f);
        int gap=P(8),x=Width-P(16),by=(Height-P(36))/2;
        foreach(var button in new[]{_more,_update,_close,_open})
        {
            // Preserve a fixed action slot while the update button is hidden.
            x-=button.Width;button.SetBounds(x,by,button.Width,P(36));x-=gap;
        }
        _more.AccessibleName="Thao tác khác cho "+Entry.Name;
        _open.AccessibleName="Mở "+Entry.Name;_close.AccessibleName="Đóng "+Entry.Name;
        _icon.SetBounds(P(18),(Height-P(44))/2,P(44),P(44));
        int left=P(76),width=Math.Max(P(80),x-left);
        _name.SetBounds(left,P(12),width,P(27));
        _version.SetBounds(left,P(42),width,P(22));
        _status.SetBounds(left,P(72),Math.Max(P(60),width-P(146)),P(22));
        _badge.SetBounds(Math.Max(left,x-P(144)),P(72),P(144),P(22));
        _badge.TextAlign=ContentAlignment.MiddleRight;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();
        base.Dispose(disposing);
    }
}
