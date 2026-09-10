using System;
using System.IO;
using System.Text;

namespace Umc2.IntakeServer
{
    /// <summary>
    /// Technical + audit logs. Never pass names, phone numbers, patient IDs or clinical text here -
    /// only record codes, usernames, device names, IP addresses and actions.
    /// </summary>
    internal static class Logs
    {
        private static readonly object Sync = new object();
        private static string directory;

        public static bool EchoToConsole { get; set; }

        public static void Init(string logDirectory)
        {
            directory = logDirectory;
            Directory.CreateDirectory(directory);
        }

        public static void Info(string message) { Write("server", "INFO  " + message); }

        public static void Warn(string message) { Write("server", "WARN  " + message); }

        public static void Error(string message, Exception ex)
        {
            Write("server", "ERROR " + message + (ex == null ? string.Empty : " | " + ex.GetType().Name + ": " + ex.Message));
        }

        public static void Audit(string actor, string action, string target, string ip)
        {
            Write("audit", string.Format("{0}\t{1}\t{2}\t{3}", Clean(actor), Clean(action), Clean(target), Clean(ip)));
        }

        private static string Clean(string value)
        {
            return string.IsNullOrEmpty(value) ? "-" : value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }

        private static void Write(string kind, string line)
        {
            var now = DateTimeOffset.Now;
            var text = now.ToString("yyyy-MM-dd HH:mm:ss zzz") + "\t" + line;
            lock (Sync)
            {
                if (EchoToConsole) Console.WriteLine(text);
                if (directory == null) return;
                try
                {
                    var file = Path.Combine(directory, kind + "-" + now.ToString("yyyyMM") + ".log");
                    File.AppendAllText(file, text + Environment.NewLine, new UTF8Encoding(false));
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
