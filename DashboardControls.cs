using System.Drawing.Drawing2D;

namespace TDNControl;

internal class SurfacePanel : Panel
{
    public SurfacePanel()
    {
        BackColor = UiKit.Card;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    internal static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right-d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right-d, r.Bottom-d, d, d, 0, 90); p.AddArc(r.X, r.Bottom-d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? UiKit.Bg);
        if (Width < 2 || Height < 2) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Round(new RectangleF(.5f,.5f,Width-1,Height-1), 14*DeviceDpi/96f);
        using var brush = new SolidBrush(BackColor);
        using var pen = new Pen(UiKit.Border);
        e.Graphics.FillPath(brush,path); e.Graphics.DrawPath(pen,path);
    }
}

internal sealed class DashboardButton : Button
{
    public bool Primary { get; }
    public bool Selected { get; set; }
    private bool _hover, _pressed;
    public DashboardButton(bool primary)
    {
        Primary = primary;
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false; Cursor = Cursors.Hand;
        BackColor = UiKit.Card; ForeColor = primary ? UiKit.Bg : UiKit.Text;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnMouseEnter(EventArgs e) { _hover=true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover=false; _pressed=false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed=e.Button==MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed=false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        if(Width<2 || Height<2)return;
        Control? canvas = Parent;
        while (canvas != null && canvas.BackColor.A != 255) canvas = canvas.Parent;
        e.Graphics.Clear(canvas?.BackColor ?? UiKit.Card);
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        Color fill = !Enabled ? Color.FromArgb(30,39,53) : Primary ? UiKit.Gold : Selected ? Color.FromArgb(57,48,33) : UiKit.CardHover;
        if(Enabled && _hover)fill=ControlPaint.Light(fill,.12f);
        if(Enabled && _pressed)fill=ControlPaint.Dark(fill,.12f);
        using var path=SurfacePanel.Round(new RectangleF(.5f,.5f,Width-1,Height-1),8*DeviceDpi/96f);
        using var brush=new SolidBrush(fill); e.Graphics.FillPath(brush,path);
        using var border=new Pen(Focused && ShowFocusCues ? UiKit.Gold : Selected ? UiKit.Gold : UiKit.Border,
            Focused && ShowFocusCues ? 2 : 1);
        e.Graphics.DrawPath(border,path);
        Color ink=!Enabled ? Color.FromArgb(139,154,175) : Primary ? UiKit.Bg : Selected ? UiKit.Gold : UiKit.Text;
        TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,ink,TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }
}
