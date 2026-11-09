namespace TDNControl;

internal static class Program
{
    private const string MutexName = "TDNMedia_TDNControl_SingleInstance";
    private const string ShowEventName = "TDNMedia_TDNControl_Show";

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        bool tray = args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));

        using var mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            // Đã có TDN Control đang chạy: gọi cửa sổ đó hiện lên rồi thoát.
            try { using var ev = EventWaitHandle.OpenExisting(ShowEventName); ev.Set(); } catch { }
            return;
        }

        if (!tray && !SelfUpdate.IsRunningFromInstallDir &&
            !args.Any(a => a.Equals("--portable", StringComparison.OrdinalIgnoreCase)))
        {
            var answer = MessageBox.Show(
                "Cài TDN Control vào máy này?\n\n• Chép vào: " + Store.InstallDir +
                "\n• Tạo lối tắt trên Desktop và Start Menu\n• Sau này tự cập nhật qua thư mục LAN\n\n" +
                "Có: cài và mở bản đã cài\nKhông: chỉ chạy lần này",
                "TDN Control · TDN Media", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) return;
            if (answer == DialogResult.Yes)
            {
                bool released = false;
                try
                {
                    // Chỉ nhả mutex ngay trước khi mở bản đã cài; lỗi thì lấy lại để không ra 2 phiên.
                    SelfUpdate.InstallSelf(() => { mutex.ReleaseMutex(); released = true; });
                    return;
                }
                catch (Exception ex)
                {
                    if (released) { try { mutex.WaitOne(0); } catch { } }
                    MessageBox.Show("Không cài được: " + ex.Message, "TDN Control", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var form = new MainForm(startHidden: tray);
        var listener = new Thread(() =>
        {
            while (true)
            {
                showEvent.WaitOne();
                if (form.IsDisposed) return;
                try { form.BeginInvoke(form.ShowFromTray); }
                catch { if (form.IsDisposed) return; } // chưa có handle / đang đóng: bỏ lượt này, vẫn nghe tiếp
            }
        }) { IsBackground = true };
        listener.Start();
        Application.Run(form);
    }
}
