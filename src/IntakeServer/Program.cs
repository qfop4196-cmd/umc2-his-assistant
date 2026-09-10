using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading;

namespace Umc2.IntakeServer
{
    internal static class Program
    {
        public const string ServiceName = "UMC2IntakeServer";

        private static int Main(string[] args)
        {
            ServerOptions options;
            try
            {
                options = ParseOptions(args);
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine(ex.Message);
                PrintUsage();
                return 2;
            }

            if (args.Contains("--help") || args.Contains("-h") || args.Contains("/?"))
            {
                PrintUsage();
                return 0;
            }
            if (args.Contains("--service"))
            {
                ServiceBase.Run(new IntakeWindowsService(options));
                return 0;
            }
            var resetIndex = Array.IndexOf(args, "--reset-password");
            if (resetIndex >= 0)
            {
                if (resetIndex + 1 >= args.Length)
                {
                    Console.Error.WriteLine("Thiếu tên đăng nhập sau --reset-password");
                    return 2;
                }
                return ResetPassword(options, args[resetIndex + 1]);
            }
            return RunConsole(options);
        }

        private static int RunConsole(ServerOptions options)
        {
            try { Console.OutputEncoding = new UTF8Encoding(false); }
            catch (IOException) { }
            Logs.EchoToConsole = true;
            var stop = new ManualResetEvent(false);
            Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                stop.Set();
            };
            using (var host = new ServerHost(options))
            {
                try
                {
                    host.Start();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Không khởi động được máy chủ: " + ex.Message);
                    Console.Error.WriteLine("Kiểm tra cổng đang bị chiếm hoặc chạy install-server.ps1 bằng quyền Administrator.");
                    return 1;
                }
                var staffPort = host.Config.Read(c => c.StaffPort);
                var publicPort = host.Config.Read(c => c.PublicPort);
                Console.WriteLine();
                Console.WriteLine("===============================================================");
                Console.WriteLine(" UMC2 Intake Server " + host.Version + " đang chạy");
                foreach (var address in host.LanAddresses())
                {
                    Console.WriteLine("  Tờ khai người bệnh : http://" + address + ":" + publicPort + "/");
                    Console.WriteLine("  Trang điều dưỡng   : http://" + address + ":" + staffPort + "/");
                }
                if (!host.Config.HasUsers)
                    Console.WriteLine(" Lần đầu: mở http://localhost:" + staffPort + "/ trên máy này để tạo tài khoản quản trị.");
                Console.WriteLine(" Nhấn Ctrl+C để dừng.");
                Console.WriteLine("===============================================================");
                stop.WaitOne();
            }
            return 0;
        }

        private static int ResetPassword(ServerOptions options, string username)
        {
            try
            {
                using (var controller = new ServiceController(ServiceName))
                {
                    if (controller.Status != ServiceControllerStatus.Stopped)
                    {
                        Console.Error.WriteLine("Dịch vụ " + ServiceName + " đang chạy. Dừng trước: net stop " + ServiceName + " (rồi net start sau khi đặt lại).");
                        return 1;
                    }
                }
            }
            catch (Exception)
            {
                // Service not installed (console mode) or not supported on this OS: nothing to stop.
            }
            var dataDir = string.IsNullOrWhiteSpace(options.DataDirectory) ? ServerHost.DefaultDataDirectory() : options.DataDirectory;
            var store = new ConfigStore(Path.Combine(Path.Combine(dataDir, "config"), "server.json"));
            store.Load();
            var temp = "Tam" + Tokens.NewDigits(6) + "x";
            var found = false;
            store.Update(c =>
            {
                var user = c.Users.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
                if (user == null) return;
                string salt, hash;
                Passwords.Hash(temp, out salt, out hash, Passwords.DefaultIterations);
                user.Salt = salt;
                user.Hash = hash;
                user.Iterations = Passwords.DefaultIterations;
                user.MustChangePassword = true;
                user.Disabled = false;
                found = true;
            });
            if (!found)
            {
                Console.Error.WriteLine("Không có tài khoản " + username);
                return 1;
            }
            Console.WriteLine("Mật khẩu tạm cho " + username + ": " + temp);
            Console.WriteLine("Người dùng sẽ phải đổi mật khẩu khi đăng nhập. Khởi động lại dịch vụ để áp dụng nếu đang chạy.");
            return 0;
        }

        private static ServerOptions ParseOptions(string[] args)
        {
            var options = new ServerOptions();
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--data":
                        options.DataDirectory = Next(args, ref i);
                        break;
                    case "--staff-port":
                        options.StaffPort = ParsePort(Next(args, ref i));
                        break;
                    case "--public-port":
                        options.PublicPort = ParsePort(Next(args, ref i));
                        break;
                    case "--bind":
                        options.BindHost = Next(args, ref i);
                        break;
                }
            }
            return options;
        }

        private static string Next(string[] args, ref int i)
        {
            if (i + 1 >= args.Length) throw new ArgumentException("Thiếu giá trị cho " + args[i]);
            return args[++i];
        }

        private static int ParsePort(string value)
        {
            int port;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out port) || port < 1 || port > 65535)
                throw new ArgumentException("Cổng không hợp lệ: " + value);
            return port;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("UMC2 Intake Server — máy chủ tờ khai trước khám (LAN + tùy chọn Internet qua Cloudflare Tunnel)");
            Console.WriteLine();
            Console.WriteLine("  IntakeServer.exe                     Chạy ở cửa sổ console (Ctrl+C để dừng)");
            Console.WriteLine("  IntakeServer.exe --service           Chế độ Windows Service (dùng bởi install-server.ps1)");
            Console.WriteLine("  IntakeServer.exe --reset-password <tên>   Cấp mật khẩu tạm cho tài khoản nhân viên");
            Console.WriteLine();
            Console.WriteLine("  Tùy chọn: --data <thư mục>  --staff-port 8080  --public-port 8081  --bind + | localhost");
        }
    }

    internal sealed class IntakeWindowsService : ServiceBase
    {
        private readonly ServerOptions options;
        private ServerHost host;

        public IntakeWindowsService(ServerOptions options)
        {
            this.options = options;
            ServiceName = Program.ServiceName;
            CanStop = true;
            CanShutdown = true;
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            host = new ServerHost(options);
            host.Start();
        }

        protected override void OnStop()
        {
            StopHost();
        }

        protected override void OnShutdown()
        {
            StopHost();
        }

        private void StopHost()
        {
            if (host == null) return;
            host.Dispose();
            host = null;
        }
    }
}
