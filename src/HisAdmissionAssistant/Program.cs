using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace HisAdmissionAssistant
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [STAThread]
        private static void Main(string[] args)
        {
            bool createdNew;
            using (var single = new Mutex(true, "Local\\UMC2-HisAdmissionAssistant", out createdNew))
            {
                if (!createdNew)
                {
                    ActivateRunningInstance();
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    MessageBox.Show(
                        "Ứng dụng gặp lỗi:\r\n" + e.Exception.Message,
                        "HIS Admission Assistant",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                };
                var form = new MainForm();
                // Autostart shortcut passes --minimized: the doctor only sees the always-on-top compact panel.
                if (args.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase)))
                    form.WindowState = FormWindowState.Minimized;
                Application.Run(form);
                GC.KeepAlive(single);
            }
        }

        private static void ActivateRunningInstance()
        {
            var current = Process.GetCurrentProcess();
            var other = Process.GetProcessesByName(current.ProcessName).FirstOrDefault(p => p.Id != current.Id);
            if (other != null && other.MainWindowHandle != IntPtr.Zero)
            {
                ShowWindow(other.MainWindowHandle, 9); // SW_RESTORE
                SetForegroundWindow(other.MainWindowHandle);
                return;
            }
            MessageBox.Show("UMC2 HIS Assistant đang chạy (xem bảng gọn ở góc màn hình hoặc thanh tác vụ).",
                "HIS Admission Assistant", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
