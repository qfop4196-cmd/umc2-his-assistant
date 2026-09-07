using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using HisAdmissionAssistant;

namespace RenderUi
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length != 1) return 2;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var form = new MainForm())
            {
                form.Show();
                Application.DoEvents();
                using (var bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0])));
                    bitmap.Save(args[0], ImageFormat.Png);
                }
                form.Close();
            }
            return 0;
        }
    }
}
