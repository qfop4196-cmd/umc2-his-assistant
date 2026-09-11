using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace Umc2.IntakeServer
{
    internal sealed class TunnelStatus
    {
        public string Mode { get; set; }
        public bool Running { get; set; }
        public string PublicUrl { get; set; }
        public string Message { get; set; }
        public bool CloudflaredFound { get; set; }
        public string StartedAt { get; set; }
    }

    /// <summary>
    /// Optional "quick tunnel" for pilots: runs cloudflared so the PATIENT port gets a temporary https://*.trycloudflare.com URL
    /// without opening any inbound port. For production use a named Cloudflare Tunnel installed as its own service
    /// (install-server.ps1 -TunnelToken) and set PublicBaseUrl. Only the public port is ever exposed.
    /// </summary>
    internal sealed class TunnelManager : IDisposable
    {
        private static readonly Regex QuickUrl = new Regex(@"https://[a-z0-9-]+\.trycloudflare\.com", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private readonly object sync = new object();
        private Process process;
        private string mode = "off";
        private string publicUrl;
        private string message = "Đang tắt";
        private string startedAt;
        private int publicPort;
        private string configuredPath;
        private bool stopping;
        private int restartCount;
        private Timer restartTimer;
        private readonly string label;

        public TunnelManager(string label)
        {
            this.label = label ?? "cổng";
        }

        public TunnelStatus Snapshot()
        {
            lock (sync)
            {
                return new TunnelStatus
                {
                    Mode = mode,
                    Running = process != null && !HasExited(process),
                    PublicUrl = publicUrl,
                    Message = message,
                    CloudflaredFound = FindCloudflared(configuredPath) != null,
                    StartedAt = startedAt
                };
            }
        }

        public void StartQuick(int port, string cloudflaredPath)
        {
            lock (sync)
            {
                publicPort = port;
                configuredPath = cloudflaredPath;
                mode = "quick";
                stopping = false;
                restartCount = 0;
                StartLocked();
            }
        }

        public void Stop()
        {
            lock (sync)
            {
                mode = "off";
                stopping = true;
                publicUrl = null;
                message = "Đang tắt";
                KillLocked();
            }
        }

        private void StartLocked()
        {
            KillLocked();
            var exe = FindCloudflared(configuredPath);
            if (exe == null)
            {
                message = "Chưa có cloudflared.exe. Chạy install-server.ps1 -WithTunnel hoặc đặt cloudflared.exe cạnh IntakeServer.exe.";
                Logs.Warn("Tunnel: " + message);
                return;
            }
            var info = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "tunnel --no-autoupdate --http-host-header localhost --url http://127.0.0.1:" + publicPort.ToString(CultureInfo.InvariantCulture),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                WorkingDirectory = Path.GetDirectoryName(exe)
            };
            try
            {
                var started = new Process { StartInfo = info, EnableRaisingEvents = true };
                started.OutputDataReceived += OnOutput;
                started.ErrorDataReceived += OnOutput;
                started.Exited += OnExited;
                started.Start();
                started.BeginOutputReadLine();
                started.BeginErrorReadLine();
                process = started;
                publicUrl = null;
                startedAt = TextUtil.Now();
                message = "Đang tạo đường hầm Cloudflare...";
                Logs.Info("Tunnel: đã khởi động cloudflared (PID " + started.Id + ") cho " + label + " " + publicPort);
            }
            catch (Exception ex)
            {
                message = "Không chạy được cloudflared: " + ex.Message;
                Logs.Error("Tunnel", ex);
            }
        }

        private void OnOutput(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            var match = QuickUrl.Match(e.Data);
            lock (sync)
            {
                if (!ReferenceEquals(sender, process)) return;
                if (match.Success && publicUrl == null)
                {
                    publicUrl = match.Value;
                    message = "Đang hoạt động (địa chỉ tạm, đổi mỗi lần khởi động lại)";
                    restartCount = 0;
                    Logs.Info("Tunnel: địa chỉ " + label + " " + publicUrl);
                }
                else if (e.Data.IndexOf(" ERR ", StringComparison.Ordinal) >= 0 && publicUrl == null)
                {
                    message = "cloudflared: " + TextUtil.FirstLine(e.Data, 160);
                }
            }
        }

        private void OnExited(object sender, EventArgs e)
        {
            lock (sync)
            {
                if (!ReferenceEquals(sender, process)) return;
                publicUrl = null;
                if (stopping || mode != "quick") return;
                restartCount++;
                var delay = TimeSpan.FromSeconds(Math.Min(300, 10 * restartCount));
                message = "cloudflared đã dừng; tự khởi động lại sau " + (int)delay.TotalSeconds + " giây.";
                Logs.Warn("Tunnel: " + message);
                if (restartTimer != null) restartTimer.Dispose();
                restartTimer = new Timer(delegate
                {
                    lock (sync)
                    {
                        if (!stopping && mode == "quick") StartLocked();
                    }
                }, null, delay, TimeSpan.FromMilliseconds(-1));
            }
        }

        private void KillLocked()
        {
            if (restartTimer != null)
            {
                restartTimer.Dispose();
                restartTimer = null;
            }
            if (process == null) return;
            var old = process;
            process = null;
            try
            {
                if (!HasExited(old)) old.Kill();
            }
            catch (Exception)
            {
            }
            finally
            {
                old.Dispose();
            }
        }

        private static bool HasExited(Process p)
        {
            try { return p.HasExited; }
            catch (Exception) { return true; }
        }

        public static string FindCloudflared(string configured)
        {
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "cloudflared.exe"),
                Path.Combine(Path.Combine(baseDir, "tools"), "cloudflared.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "cloudflared\\cloudflared.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "cloudflared\\cloudflared.exe")
            };
            var found = candidates.FirstOrDefault(File.Exists);
            if (found != null) return found;
            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var dir in path.Split(Path.PathSeparator))
            {
                try
                {
                    var exe = Path.Combine(dir.Trim(), "cloudflared.exe");
                    if (File.Exists(exe)) return exe;
                    var unix = Path.Combine(dir.Trim(), "cloudflared");
                    if (Path.DirectorySeparatorChar == '/' && File.Exists(unix)) return unix;
                }
                catch (ArgumentException)
                {
                }
            }
            return null;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
