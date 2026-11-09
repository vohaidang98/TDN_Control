namespace TDNControl;

/// <summary>Cửa sổ Cài đặt: thư mục cập nhật, chạy cùng Windows, khay, báo bản mới, danh sách app hiện/ẩn.</summary>
internal sealed class SettingsForm : Form
{
    private readonly ControlSettings _settings;
    private readonly TextBox _folder = new();
    private readonly CheckBox _startup = new();
    private readonly CheckBox _tray = new();
    private readonly CheckBox _notify = new();
    private readonly CheckedListBox _list = new();
    private readonly HashSet<string> _hidden;
    private string _listFolder = "";
    private int _loadId;
    private readonly Label _listStatus = UiKit.MakeLabel("", UiKit.Muted, 9f);

    public SettingsForm(ControlSettings settings)
    {
        _settings = settings;
        _hidden = new HashSet<string>(settings.HiddenApps ?? new(), StringComparer.OrdinalIgnoreCase);
        Text = "Cài đặt · TDN Control";
        Font = UiKit.Font();
        BackColor = UiKit.Bg; ForeColor = UiKit.Text;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        ClientSize = new Size(640, 640);
        AutoScaleDimensions = new SizeF(96F,96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        var ico = UiKit.LoadIcon("app.ico"); if (ico != null) Icon = ico;

        var root = new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=1, RowCount=6,
            Padding=new Padding(24), BackColor=UiKit.Bg };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        foreach(int h in new[]{68,86,112,46})root.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,56));
        Controls.Add(root);
        var intro=new Panel {Dock=DockStyle.Fill};
        var title=UiKit.MakeLabel("Cài đặt không gian làm việc",UiKit.Text,18f,FontStyle.Bold);title.Dock=DockStyle.Top;title.Height=34;
        var hint=UiKit.MakeLabel("Nguồn cập nhật và tùy chọn hoạt động của TDN Control.",UiKit.Muted);hint.Dock=DockStyle.Top;hint.Height=26;
        intro.Controls.Add(hint);intro.Controls.Add(title);root.Controls.Add(intro,0,0);
        var folderArea=new Panel{Dock=DockStyle.Fill};
        var lblFolder=UiKit.MakeLabel("THƯ MỤC CẬP NHẬT LAN",UiKit.Muted,9f,FontStyle.Bold);lblFolder.Dock=DockStyle.Top;lblFolder.Height=28;
        var folderRow=new TableLayoutPanel{Dock=DockStyle.Top,Height=38,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100));
        folderRow.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var folderHost=new SurfacePanel{Dock=DockStyle.Fill,Padding=new Padding(10,8,10,6),Margin=new Padding(0,0,8,0)};
        _folder.Dock=DockStyle.Fill;_folder.Text=settings.UpdateFolder;_folder.BackColor=UiKit.Card;_folder.ForeColor=UiKit.Text;_folder.BorderStyle=BorderStyle.None;
        _folder.AccessibleName="Thư mục cập nhật LAN";_folder.Leave+=(_,_)=>LoadList();folderHost.Controls.Add(_folder);
        var pick=UiKit.MakeButton("Chọn…",false,100,38);pick.Dock=DockStyle.Fill;pick.Margin=Padding.Empty;
        pick.Click+=(_,_)=>{using var dlg=new FolderBrowserDialog{Description="Chọn thư mục cập nhật (chứa các thư mục app)",SelectedPath=_folder.Text};
            if(dlg.ShowDialog(this)==DialogResult.OK){_folder.Text=dlg.SelectedPath;LoadList();}};
        folderRow.Controls.Add(folderHost,0,0);folderRow.Controls.Add(pick,1,0);folderArea.Controls.Add(folderRow);folderArea.Controls.Add(lblFolder);root.Controls.Add(folderArea,0,1);
        var options=new SurfacePanel{Dock=DockStyle.Fill,Padding=new Padding(14,8,14,8),Margin=Padding.Empty};
        bool canStartup=Startup.CanEnable;
        Style(_startup,canStartup?"Khởi động cùng Windows, thu nhỏ xuống khay":"Khởi động cùng Windows (cần cài app trước)",settings.StartWithWindows&&canStartup,8);_startup.Enabled=canStartup;
        Style(_tray,"Thu xuống khay hệ thống khi bấm đóng cửa sổ",settings.CloseToTray,38);
        Style(_notify,"Thông báo khi có phiên bản ứng dụng mới",settings.NotifyUpdates,68);
        options.Controls.AddRange(new Control[]{_startup,_tray,_notify});root.Controls.Add(options,0,2);
        var listHeader=new Panel{Dock=DockStyle.Fill};
        var lblApps=UiKit.MakeLabel("Ứng dụng hiển thị",UiKit.Text,11f,FontStyle.Bold);lblApps.Dock=DockStyle.Left;lblApps.Width=260;
        _listStatus.Dock=DockStyle.Fill;_listStatus.TextAlign=ContentAlignment.MiddleRight;listHeader.Controls.Add(_listStatus);listHeader.Controls.Add(lblApps);root.Controls.Add(listHeader,0,3);
        _list.Dock=DockStyle.Fill;_list.BackColor=UiKit.Card;_list.ForeColor=UiKit.Text;_list.BorderStyle=BorderStyle.None;
        _list.Font=UiKit.Font(11f);_list.CheckOnClick=true;_list.IntegralHeight=false;_list.AccessibleName="Chọn ứng dụng hiển thị trên bảng điều khiển";
        _list.ItemCheck+=(_,e)=>{string id=_list.Items[e.Index]?.ToString()??"";if(e.NewValue==CheckState.Checked)_hidden.Remove(id);else _hidden.Add(id);};
        root.Controls.Add(_list,0,4);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(0,16,0,0),Margin=Padding.Empty};
        var save=UiKit.MakeButton("Lưu thay đổi",true,144,38);save.Click+=(_,_)=>Save();
        var cancel=UiKit.MakeButton("Hủy",false,100,38);cancel.Click+=(_,_)=>{DialogResult=DialogResult.Cancel;Close();};
        actions.Controls.AddRange(new Control[]{save,cancel});root.Controls.Add(actions,0,5);AcceptButton=save;CancelButton=cancel;
        LoadList();
    }

    private void Style(CheckBox c, string text, bool value, int y)
    {
        c.Text = text; c.Checked = value; c.AutoSize = false;
        c.SetBounds(20, y, 520, 24); c.ForeColor = UiKit.Text; c.BackColor = Color.Transparent;
    }

    private static List<string> ReadIds(string folder)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Không truy cập được thư mục này.");
        return Directory.GetDirectories(folder).Select(d => Path.GetFileName(d))
            .Where(n => !n.StartsWith('_') && !n.StartsWith('.') &&
                        !n.Equals(AppInfo.SelfFolderName, StringComparison.OrdinalIgnoreCase) &&
                        Store.ReadJson<AppOverride>(Path.Combine(folder,n,"tdn-app.json"))?.Hidden != true)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Đọc danh sách thư mục app ở luồng nền; lỗi/mất mạng chỉ hiện chữ, vẫn sửa đường dẫn và Lưu được.</summary>
    private async void LoadList()
    {
        string folder = _folder.Text.Trim();
        if (folder == _listFolder) return;
        _listFolder = folder;
        int id = ++_loadId;
        _list.Items.Clear();
        _listStatus.Text = "Đang đọc..."; _listStatus.ForeColor = UiKit.Muted;
        List<string>? ids = null; string? error = null;
        try { ids = await Task.Run(() => ReadIds(folder)); }
        catch (Exception ex) { error = ex.Message; }
        if (IsDisposed || id != _loadId) return;
        if (ids != null)
        {
            foreach (string name in ids) _list.Items.Add(name, !_hidden.Contains(name));
            _listStatus.Text = "";
        }
        else
        {
            _listStatus.Text = "Không đọc được: " + error; _listStatus.ForeColor = UiKit.Orange;
        }
    }

    private void Save()
    {
        string folder = _folder.Text.Trim();
        if (folder.Length == 0)
        {
            MessageBox.Show(this, "Hãy nhập thư mục cập nhật.", "Cài đặt", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _settings.UpdateFolder = folder;
        _settings.CloseToTray = _tray.Checked;
        _settings.NotifyUpdates = _notify.Checked;
        if (_settings.StartWithWindows != _startup.Checked)
        {
            _settings.StartWithWindows = _startup.Checked;
            Startup.Apply(_startup.Checked);
        }
        _settings.HiddenApps = _hidden.ToList();
        Store.Save(_settings);
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); UiKit.DarkTitleBar(Handle); }
}
