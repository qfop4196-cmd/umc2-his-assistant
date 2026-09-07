using System;
using System.Threading;
using System.Windows.Forms;

namespace HisAdmissionAssistant
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
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
            Application.Run(new MainForm());
        }
    }
}
