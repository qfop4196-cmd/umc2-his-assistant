using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;

namespace Umc2.IntakeServer
{
    internal sealed class ServerOptions
    {
        public string DataDirectory { get; set; }
        public int StaffPort { get; set; }
        public int PublicPort { get; set; }
        public string BindHost { get; set; }
        /// <summary>--demo: staff page reachable through a second quick tunnel (sample data only) and sample records seeded.</summary>
        public bool Demo { get; set; }
    }

    /// <summary>Composition root: wires config, encrypted store, both HTTP ports, discovery, tunnel and maintenance.</summary>
    internal sealed class ServerHost : IDisposable
    {
        private readonly ServerOptions options;
        private HttpHost publicHost;
        private HttpHost staffHost;
        private DiscoveryResponder discovery;
        private Timer maintenanceTimer;
        private long changeStamp;

        public ServerHost(ServerOptions options)
        {
            this.options = options;
            DataDirectory = string.IsNullOrWhiteSpace(options.DataDirectory) ? DefaultDataDirectory() : Path.GetFullPath(options.DataDirectory);
            LogDirectory = Path.Combine(DataDirectory, "logs");
            Version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            Sessions = new SessionManager();
            Limiter = new RateLimiter();
            Pairing = new PairingCodes();
            Tunnel = new TunnelManager("cổng người bệnh");
            StaffTunnel = new TunnelManager("cổng nhân viên (demo)");
            changeStamp = DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond;
        }

        public string DataDirectory { get; private set; }
        public string LogDirectory { get; private set; }
        public string Version { get; private set; }
        public ConfigStore Config { get; private set; }
        public IntakeStore Store { get; private set; }
        public SessionManager Sessions { get; private set; }
        public RateLimiter Limiter { get; private set; }
        public PairingCodes Pairing { get; private set; }
        public StaticFiles Files { get; private set; }
        public DataProtector Protector { get; private set; }
        public AiAssistant Ai { get; private set; }
        public TunnelManager Tunnel { get; private set; }
        /// <summary>Demo only: quick tunnel in front of the staff port so judges/reviewers can open the nurse page from the Internet.</summary>
        public TunnelManager StaffTunnel { get; private set; }

        /// <summary>Increments whenever queue content changes; clients use it to skip needless refreshes.</summary>
        public long ChangeStamp { get { return Interlocked.Read(ref changeStamp); } }

        public bool LocalOnly
        {
            get { return (staffHost != null && staffHost.LocalOnly) || (publicHost != null && publicHost.LocalOnly); }
        }

        public static string DefaultDataDirectory()
        {
            return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "UMC2"), "IntakeServer");
        }

        public void NotifyChanged()
        {
            Interlocked.Increment(ref changeStamp);
        }

        public void Start()
        {
            Directory.CreateDirectory(DataDirectory);
            Logs.Init(LogDirectory);
            Logs.Info("Khởi động UMC2 Intake Server " + Version + " — dữ liệu: " + DataDirectory);

            var configDir = Path.Combine(DataDirectory, "config");
            Config = new ConfigStore(Path.Combine(configDir, "server.json"));
            Config.Load();
            if (options.StaffPort > 0 || options.PublicPort > 0 || !string.IsNullOrWhiteSpace(options.BindHost) || options.Demo)
            {
                Config.Update(c =>
                {
                    if (options.StaffPort > 0) c.StaffPort = options.StaffPort;
                    if (options.PublicPort > 0) c.PublicPort = options.PublicPort;
                    if (!string.IsNullOrWhiteSpace(options.BindHost)) c.BindHost = options.BindHost.Trim();
                    if (options.Demo)
                    {
                        c.AllowPublicStaffAccess = true;
                        c.TunnelMode = "quick";
                    }
                });
            }

            var protector = new DataProtector(Path.Combine(configDir, "data.key"));
            Protector = protector;
            Ai = new AiAssistant(this);
            Store = new IntakeStore(Path.Combine(DataDirectory, "intakes"), protector);
            var loaded = Store.Load();
            Logs.Info("Đã nạp " + loaded + " tờ khai (đã mã hóa).");
            if (options.Demo && loaded == 0)
            {
                var seeded = DemoData.Seed(Store, "system");
                Logs.Info("Chế độ demo: đã tạo " + seeded + " tờ khai mẫu (dữ liệu giả).");
            }
            Maintenance(null);

            Files = new StaticFiles();
            var bind = Config.Read(c => c.BindHost);
            publicHost = new HttpHost("Cổng người bệnh", PortKind.Public, bind, Config.Read(c => c.PublicPort), new PublicEndpoints(this).Handle);
            staffHost = new HttpHost("Cổng nhân viên", PortKind.Staff, bind, Config.Read(c => c.StaffPort), new StaffEndpoints(this).Handle);
            publicHost.Start();
            staffHost.Start();

            if (Config.Read(c => c.DiscoveryEnabled))
            {
                discovery = new DiscoveryResponder(Config.Read(c => c.DiscoveryPort), Describe);
                discovery.Start();
            }
            ApplyTunnelMode();

            maintenanceTimer = new Timer(Maintenance, null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30));
        }

        /// <summary>Starts/stops cloudflared for the patient port and, in demo mode, for the staff port too.</summary>
        public void ApplyTunnelMode()
        {
            var quick = Config.Read(c => c.TunnelMode) == "quick";
            var cloudflared = Config.Read(c => c.CloudflaredPath);
            if (quick) Tunnel.StartQuick(Config.Read(c => c.PublicPort), cloudflared); else Tunnel.Stop();
            if (quick && Config.Read(c => c.AllowPublicStaffAccess)) StaffTunnel.StartQuick(Config.Read(c => c.StaffPort), cloudflared);
            else StaffTunnel.Stop();
        }

        private Dictionary<string, object> Describe()
        {
            return new Dictionary<string, object>
            {
                { "service", "umc2-intake" },
                { "version", Version },
                { "hospitalName", Config.Read(c => c.HospitalName) },
                { "staffPort", Config.Read(c => c.StaffPort) },
                { "publicPort", Config.Read(c => c.PublicPort) }
            };
        }

        private void Maintenance(object state)
        {
            try
            {
                var purged = Store.Purge(Config.Read(c => c.RetentionDaysPending), Config.Read(c => c.RetentionDaysDone));
                if (purged.Count > 0)
                {
                    Logs.Audit("system", "purge", purged.Count + " tờ khai hết hạn", "-");
                    NotifyChanged();
                }
                var released = Store.ReleaseStaleClaims(TimeSpan.FromHours(4));
                if (released.Count > 0)
                {
                    Logs.Audit("system", "claim-expired", string.Join(",", released.ToArray()), "-");
                    NotifyChanged();
                }
            }
            catch (Exception ex)
            {
                Logs.Error("Bảo trì định kỳ", ex);
            }
        }

        /// <summary>IPv4 addresses of active LAN adapters (for URLs/QR codes shown to staff).</summary>
        public IList<string> LanAddresses()
        {
            var result = new List<string>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback || nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                    {
                        var address = unicast.Address;
                        if (address.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (System.Net.IPAddress.IsLoopback(address)) continue;
                        var bytes = address.GetAddressBytes();
                        if (bytes[0] == 169 && bytes[1] == 254) continue;
                        var text = address.ToString();
                        if (!result.Contains(text)) result.Add(text);
                    }
                }
            }
            catch (NetworkInformationException ex)
            {
                Logs.Error("Liệt kê địa chỉ mạng", ex);
            }
            if (LocalOnly || result.Count == 0) result.Insert(0, "localhost");
            return result.OrderBy(a => a == "localhost" ? 1 : (NetUtil.IsPrivateOrLoopback(System.Net.IPAddress.Parse(a)) ? 0 : 2)).ToList();
        }

        public void Dispose()
        {
            if (maintenanceTimer != null) maintenanceTimer.Dispose();
            if (Tunnel != null) Tunnel.Dispose();
            if (StaffTunnel != null) StaffTunnel.Dispose();
            if (discovery != null) discovery.Dispose();
            if (staffHost != null) staffHost.Dispose();
            if (publicHost != null) publicHost.Dispose();
            Logs.Info("Đã dừng máy chủ.");
        }
    }
}
