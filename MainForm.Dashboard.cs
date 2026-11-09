namespace TDNControl;

public sealed partial class MainForm
{
    private readonly TextBox _search = new();
    private readonly Label _totalValue = UiKit.MakeLabel("0", UiKit.Text, 25f, FontStyle.Bold);
    private readonly Label _runningValue = UiKit.MakeLabel("0", UiKit.Green, 25f, FontStyle.Bold);
    private readonly Label _updatesValue = UiKit.MakeLabel("0", UiKit.Gold, 25f, FontStyle.Bold);
    private readonly Label _sourceStatus = UiKit.MakeLabel("Nguồn cập nhật LAN", UiKit.Green, 9f);
    private readonly Label _sourcePath = UiKit.MakeLabel("", UiKit.Muted, 9f);
    private readonly Label _listCount = UiKit.MakeLabel("Đang đọc danh sách…", UiKit.Muted, 9f);
    private readonly Label _filterEmpty = UiKit.MakeLabel("Không có ứng dụng phù hợp. Thử đổi từ khóa hoặc bộ lọc.", UiKit.Muted);
    private readonly List<DashboardButton> _filterButtons = new();
    private int _appFilter;
    private bool _fittingRows;

    private void BuildDashboard()
    {
        var shell = new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=2, RowCount=1, Margin=Padding.Empty };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,190));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        Controls.Add(shell);
        var sidebar = new Panel { Dock=DockStyle.Fill, BackColor=Color.FromArgb(10,15,24), Margin=Padding.Empty, Padding=new Padding(18,20,18,20) };
        shell.Controls.Add(sidebar,0,0);
        var brand = new PictureBox { Location=new Point(22,26), Size=new Size(48,48), SizeMode=PictureBoxSizeMode.Zoom,
            Image=UiKit.LoadImage("logo-d.png"), BackColor=Color.Transparent };
        sidebar.Controls.Add(brand);
        var brandName=UiKit.MakeLabel("TDN MEDIA",UiKit.Gold,10f,FontStyle.Bold); brandName.SetBounds(22,84,150,24);
        var brandHint=UiKit.MakeLabel("WORKSPACE",UiKit.Muted,8.5f); brandHint.SetBounds(22,111,150,20);
        sidebar.Controls.AddRange(new Control[]{brandName,brandHint});
        var navigation = new FlowLayoutPanel { Location=new Point(16,166), Size=new Size(158,156), FlowDirection=FlowDirection.TopDown,
            WrapContents=false, BackColor=Color.Transparent };
        var home = new DashboardButton(false) { Text="Ứng dụng", Selected=true, Size=new Size(154,42), Font=UiKit.Font(10f,FontStyle.Bold), Margin=new Padding(0,0,0,10) };
        var render = UiKit.MakeButton("Render LAN",false,154,42); render.Margin=new Padding(0,0,0,10);
        var settings = UiKit.MakeButton("Cài đặt",false,154,42); settings.Margin=Padding.Empty;
        home.Click+=(_,_)=>{_search.Clear();_appFilter=0;ApplyAppFilter();_search.Focus();};
        render.Click+=async(_,_)=>{await ReadLanAsync();ShowRenderStatus();};
        settings.Click+=(_,_)=>OpenSettings();
        navigation.Controls.AddRange(new Control[]{home,render,settings}); sidebar.Controls.Add(navigation);
        var source = new Panel { Dock=DockStyle.Bottom, Height=142, BackColor=Color.Transparent };
        _sourceStatus.Dock=DockStyle.Top;_sourceStatus.Height=28;
        _sourcePath.Dock=DockStyle.Top;_sourcePath.Height=64;_sourcePath.TextAlign=ContentAlignment.TopLeft;
        var edition=UiKit.MakeLabel($"TDN Control  /  v{AppInfo.Version}",UiKit.Muted,8.5f);edition.Dock=DockStyle.Bottom;edition.Height=24;
        source.Controls.Add(_sourcePath);source.Controls.Add(_sourceStatus);source.Controls.Add(edition);sidebar.Controls.Add(source);

        var content = new Panel { Dock=DockStyle.Fill, Padding=new Padding(24,12,24,12), BackColor=UiKit.Bg, Margin=Padding.Empty };
        shell.Controls.Add(content,1,0);
        var header=new TableLayoutPanel { Dock=DockStyle.Top, Height=86, ColumnCount=2, RowCount=1, BackColor=UiKit.Bg, Margin=Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,288));
        header.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var titles=new Panel { Dock=DockStyle.Fill, Margin=Padding.Empty };
        var title=UiKit.MakeLabel("Trung tâm ứng dụng",UiKit.Text,22f,FontStyle.Bold);title.Dock=DockStyle.Top;title.Height=43;
        var subtitle=UiKit.MakeLabel("Quản lý công cụ. Đồng bộ cập nhật. Sẵn sàng làm việc.",UiKit.Muted,9.5f);subtitle.Dock=DockStyle.Top;subtitle.Height=28;
        titles.Controls.Add(subtitle);titles.Controls.Add(title);header.Controls.Add(titles,0,0);
        var headerActions=new FlowLayoutPanel { Dock=DockStyle.Fill, Padding=new Padding(0,14,0,0), WrapContents=false, Margin=Padding.Empty };
        _btnRefresh.Text="Kiểm tra lại";_btnRefresh.Size=new Size(116,38);
        _btnUpdateAll.Text="Cập nhật tất cả";_btnUpdateAll.Size=new Size(160,38);_btnUpdateAll.Enabled=false;
        _btnRefresh.Click+=async(_,_)=>await ScanAsync(manual:true);_btnUpdateAll.Click+=async(_,_)=>await UpdateAllAsync();
        headerActions.Controls.AddRange(new Control[]{_btnRefresh,_btnUpdateAll});header.Controls.Add(headerActions,1,0);

        var metrics=new TableLayoutPanel { Dock=DockStyle.Top, Height=116, ColumnCount=3, RowCount=1, BackColor=UiKit.Bg, Margin=Padding.Empty };
        metrics.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        for(int i=0;i<3;i++)metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/3));
        metrics.Controls.Add(Metric("ỨNG DỤNG",_totalValue,"Trong không gian làm việc"),0,0);
        metrics.Controls.Add(Metric("ĐANG CHẠY",_runningValue,"Đang hoạt động trên máy"),1,0);
        metrics.Controls.Add(Metric("BẢN CẬP NHẬT",_updatesValue,"Ứng dụng có phiên bản mới"),2,0);

        _banner.Dock=DockStyle.Top;_banner.Height=48;_banner.BackColor=UiKit.Banner;_banner.Visible=false;
        _bannerButton.Dock=DockStyle.Right;_bannerButton.Width=140;_bannerButton.Margin=new Padding(4);
        _bannerLabel.Dock=DockStyle.Fill;_bannerLabel.Padding=new Padding(12,0,8,0);
        _banner.Controls.Add(_bannerLabel);_banner.Controls.Add(_bannerButton);
        _bannerButton.Click+=async(_,_)=>await SelfUpdateAsync();

        var toolbar=new TableLayoutPanel { Dock=DockStyle.Top, Height=58, ColumnCount=2, RowCount=1, Padding=new Padding(0,10,0,10), Margin=Padding.Empty };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,250));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var filters=new FlowLayoutPanel { Dock=DockStyle.Fill, WrapContents=false, Margin=Padding.Empty };
        var names=new[]{"Tất cả","Đang chạy","Có cập nhật"};
        for(int i=0;i<names.Length;i++)
        {
            int filter=i;var button=new DashboardButton(false){Text=names[i],Size=new Size(i==0?76:108,36),Font=UiKit.Font(9f),Margin=new Padding(0,0,6,0)};
            button.Click+=(_,_)=>{_appFilter=filter;ApplyAppFilter();};_filterButtons.Add(button);filters.Controls.Add(button);
        }
        toolbar.Controls.Add(filters,0,0);
        var searchHost=new SurfacePanel { Dock=DockStyle.Fill, Padding=new Padding(12,8,10,7), Margin=Padding.Empty };
        _search.Dock=DockStyle.Fill;_search.BorderStyle=BorderStyle.None;_search.BackColor=UiKit.Card;_search.ForeColor=UiKit.Text;
        _search.Font=UiKit.Font(10f);_search.PlaceholderText="Tìm ứng dụng…";_search.AccessibleName="Tìm ứng dụng theo tên";
        _search.TextChanged+=(_,_)=>ApplyAppFilter();searchHost.Controls.Add(_search);toolbar.Controls.Add(searchHost,1,0);
        var searchHint=UiKit.MakeLabel("Tìm",UiKit.Muted,9.5f);
        searchHint.Dock=DockStyle.Left;searchHint.Width=36;searchHint.Cursor=Cursors.IBeam;
        searchHost.Controls.Add(searchHint);searchHint.Click+=(_,_)=>_search.Focus();

        var listHost=new Panel { Dock=DockStyle.Fill, BackColor=UiKit.Bg };
        _listCount.Dock=DockStyle.Top;_listCount.Height=28;
        _list.Dock=DockStyle.Fill;_list.FlowDirection=FlowDirection.TopDown;_list.WrapContents=false;_list.AutoScroll=true;_list.BackColor=UiKit.Bg;
        _list.Resize+=(_,_)=>FitRows();
        _list.Layout+=(_,_)=>FitRows();
        _filterEmpty.Dock=DockStyle.Fill;_filterEmpty.TextAlign=ContentAlignment.MiddleCenter;_filterEmpty.Visible=false;
        _emptyPanel.Dock=DockStyle.Fill;_emptyPanel.BackColor=UiKit.Bg;_emptyPanel.Visible=false;
        _emptyLabel.Dock=DockStyle.Top;_emptyLabel.Height=130;_emptyLabel.TextAlign=ContentAlignment.BottomCenter;
        var emptyButton=UiKit.MakeButton("Mở cài đặt",true,150,38);emptyButton.Click+=(_,_)=>OpenSettings();
        _emptyPanel.Controls.AddRange(new Control[]{emptyButton,_emptyLabel});
        _emptyPanel.Resize+=(_,_)=>emptyButton.Location=new Point(Math.Max(0,(_emptyPanel.Width-emptyButton.Width)/2),_emptyLabel.Bottom+14);
        listHost.Controls.Add(_list);listHost.Controls.Add(_filterEmpty);listHost.Controls.Add(_emptyPanel);listHost.Controls.Add(_listCount);

        var footer=new Panel{Dock=DockStyle.Bottom,Height=30,BackColor=UiKit.Bg};
        _footerStatus.Dock=DockStyle.Fill;_footerStatus.TextAlign=ContentAlignment.MiddleRight;
        var credit=UiKit.MakeLabel("TDN MEDIA  /  CONTROL",UiKit.Muted,8f);credit.Dock=DockStyle.Left;credit.Width=160;
        footer.Controls.Add(_footerStatus);footer.Controls.Add(credit);
        content.Controls.Add(listHost);content.Controls.Add(footer);
        content.Controls.Add(toolbar);content.Controls.Add(_banner);content.Controls.Add(metrics);content.Controls.Add(header);
        UpdateFooter();ApplyAppFilter();
    }

    private static Control Metric(string caption,Label value,string detail)
    {
        var panel=new SurfacePanel{Dock=DockStyle.Fill,Padding=new Padding(16,8,16,8),Margin=new Padding(0,0,10,8)};
        var title=UiKit.MakeLabel(caption,UiKit.Muted,8.5f,FontStyle.Bold);title.Dock=DockStyle.Top;title.Height=22;
        value.Dock=DockStyle.Top;value.Height=43;
        var hint=UiKit.MakeLabel(detail,UiKit.Muted,8.5f);hint.Dock=DockStyle.Top;hint.Height=22;
        panel.Controls.Add(hint);panel.Controls.Add(value);panel.Controls.Add(title);return panel;
    }

    private void UpdateDashboardSummary()
    {
        _totalValue.Text=_apps.Count.ToString();_runningValue.Text=_apps.Count(a=>a.Running).ToString();
        _updatesValue.Text=_apps.Count(a=>a.HasUpdate).ToString();
    }

    private void ShowRenderStatus()
    {
        using var dialog=new Form { Text="Render LAN · TDN Control", StartPosition=FormStartPosition.CenterParent,
            ClientSize=new Size(720,420), MinimumSize=new Size(540,320), BackColor=UiKit.Bg, ForeColor=UiKit.Text,
            Font=UiKit.Font(), Padding=new Padding(20), ShowInTaskbar=false, MinimizeBox=false };
        var title=UiKit.MakeLabel("Trạng thái Render LAN",UiKit.Text,16f,FontStyle.Bold);title.Dock=DockStyle.Top;title.Height=46;
        var body=new TextBox { Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Both,
            WordWrap=false,BackColor=UiKit.Card,ForeColor=UiKit.Text,BorderStyle=BorderStyle.None,
            Font=UiKit.Font(10f),Text=_renderBody.Text.Replace("\n",Environment.NewLine),AccessibleName="Chi tiết trạng thái Render LAN" };
        var actions=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=54,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(0,12,0,0)};
        var close=UiKit.MakeButton("Đóng",false,100,38);close.Click+=(_,_)=>dialog.Close();
        var refresh=UiKit.MakeButton("Làm mới",true,120,38);
        refresh.Click+=async(_,_)=>{refresh.Enabled=false;await ReadLanAsync();if(!dialog.IsDisposed){body.Text=_renderBody.Text.Replace("\n",Environment.NewLine);refresh.Enabled=true;}};
        actions.Controls.AddRange(new Control[]{close,refresh});dialog.Controls.Add(body);dialog.Controls.Add(actions);dialog.Controls.Add(title);
        dialog.CancelButton=close;dialog.HandleCreated+=(_,_)=>UiKit.DarkTitleBar(dialog.Handle);dialog.ShowDialog(this);
    }

    private void ApplyAppFilter()
    {
        if(_filterButtons.Count==0)return;
        string query=_search.Text.Trim();int visible=0;
        _list.SuspendLayout();
        foreach(var row in _rows.Values)
        {
            bool show=(query.Length==0 || row.Entry.Name.Contains(query,StringComparison.CurrentCultureIgnoreCase)) &&
                (_appFilter==0 || _appFilter==1 && row.Entry.Running || _appFilter==2 && row.Entry.HasUpdate);
            row.Visible=show;if(show)visible++;
        }
        for(int i=0;i<_filterButtons.Count;i++){_filterButtons[i].Selected=i==_appFilter;_filterButtons[i].Invalidate();}
        _listCount.Text=$"{visible} / {_apps.Count} ứng dụng";
        _filterEmpty.Visible=_apps.Count>0 && visible==0;
        if(_filterEmpty.Visible)_filterEmpty.BringToFront();
        _list.ResumeLayout(true);FitRows();
    }
}
