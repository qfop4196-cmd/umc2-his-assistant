using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Umc2.IntakeServer
{
    /// <summary>Server settings + staff accounts + paired devices. Secrets are stored only as hashes.</summary>
    public sealed class ServerConfig
    {
        public ServerConfig()
        {
            HospitalName = "Bệnh viện";
            DepartmentName = string.Empty;
            StaffPort = 8080;
            PublicPort = 8081;
            BindHost = "+";
            PublicBaseUrl = string.Empty;
            TunnelMode = "off";
            CloudflaredPath = string.Empty;
            DiscoveryEnabled = true;
            DiscoveryPort = 47810;
            RetentionDaysPending = 3;
            RetentionDaysDone = 7;
            MaxPending = 500;
            PublicSubmitLimitPerIp = 6;
            RequireHisPatientIdOnApprove = false;
            AllowPublicStaffAccess = false;
            AiEnabled = false;
            AiModel = AiAssistant.DefaultModel;
            AiEndpoint = string.Empty;
            AiApiKeyProtected = string.Empty;
            Users = new List<StaffUser>();
            Devices = new List<AgentDevice>();
            ConfigVersion = 1;
        }

        public int ConfigVersion { get; set; }
        public string HospitalName { get; set; }
        public string DepartmentName { get; set; }
        public int StaffPort { get; set; }
        public int PublicPort { get; set; }
        /// <summary>"+" = all interfaces (needs urlacl, done by install-server.ps1); "localhost" = this PC only.</summary>
        public string BindHost { get; set; }
        /// <summary>Public https URL of the patient form (Cloudflare named tunnel, reverse proxy...). Used for QR codes.</summary>
        public string PublicBaseUrl { get; set; }
        /// <summary>off | quick (temporary trycloudflare.com URL managed by this server).</summary>
        public string TunnelMode { get; set; }
        public string CloudflaredPath { get; set; }
        public bool DiscoveryEnabled { get; set; }
        public int DiscoveryPort { get; set; }
        public int RetentionDaysPending { get; set; }
        public int RetentionDaysDone { get; set; }
        public int MaxPending { get; set; }
        public int PublicSubmitLimitPerIp { get; set; }
        public bool RequireHisPatientIdOnApprove { get; set; }
        /// <summary>Must stay false in production: staff/agent API only answers LAN clients.</summary>
        public bool AllowPublicStaffAccess { get; set; }
        /// <summary>Controlled AI helper on the nurse page (drafts + questions about one record). Key stored DPAPI-protected.</summary>
        public bool AiEnabled { get; set; }
        public string AiModel { get; set; }
        public string AiEndpoint { get; set; }
        public string AiApiKeyProtected { get; set; }
        public List<StaffUser> Users { get; set; }
        public List<AgentDevice> Devices { get; set; }
    }

    internal sealed class ConfigStore
    {
        private readonly string path;
        private readonly object sync = new object();
        private ServerConfig config;

        public ConfigStore(string path)
        {
            this.path = path;
        }

        public string Path { get { return path; } }

        public void Load()
        {
            lock (sync)
            {
                if (File.Exists(path))
                {
                    config = Json.Deserialize<ServerConfig>(File.ReadAllText(path, Encoding.UTF8)) ?? new ServerConfig();
                    if (config.Users == null) config.Users = new List<StaffUser>();
                    if (config.Devices == null) config.Devices = new List<AgentDevice>();
                }
                else
                {
                    config = new ServerConfig();
                    SaveLocked();
                }
                Sanitize(config);
            }
        }

        /// <summary>Runs <paramref name="read"/> under the config lock. Do not keep references to returned lists.</summary>
        public T Read<T>(Func<ServerConfig, T> read)
        {
            lock (sync) return read(config);
        }

        public void Update(Action<ServerConfig> mutate)
        {
            lock (sync)
            {
                mutate(config);
                Sanitize(config);
                SaveLocked();
            }
        }

        public bool HasUsers
        {
            get { lock (sync) return config.Users.Any(u => !u.Disabled); }
        }

        private void SaveLocked()
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, Json.Indent(Json.Serialize(config)), new UTF8Encoding(false));
            FileUtil.ReplaceFile(tmp, path);
        }

        private static void Sanitize(ServerConfig c)
        {
            if (c.StaffPort < 1 || c.StaffPort > 65535) c.StaffPort = 8080;
            if (c.PublicPort < 1 || c.PublicPort > 65535) c.PublicPort = 8081;
            if (c.PublicPort == c.StaffPort) c.PublicPort = c.StaffPort + 1;
            if (string.IsNullOrWhiteSpace(c.BindHost)) c.BindHost = "+";
            if (c.RetentionDaysPending < 1) c.RetentionDaysPending = 1;
            if (c.RetentionDaysPending > 30) c.RetentionDaysPending = 30;
            if (c.RetentionDaysDone < 1) c.RetentionDaysDone = 1;
            if (c.RetentionDaysDone > 90) c.RetentionDaysDone = 90;
            if (c.MaxPending < 10) c.MaxPending = 10;
            if (c.PublicSubmitLimitPerIp < 1) c.PublicSubmitLimitPerIp = 1;
            if (c.TunnelMode != "quick") c.TunnelMode = "off";
            if (string.IsNullOrWhiteSpace(c.HospitalName)) c.HospitalName = "Bệnh viện";
            if (c.DiscoveryPort < 1 || c.DiscoveryPort > 65535) c.DiscoveryPort = 47810;
        }
    }

    internal static class FileUtil
    {
        /// <summary>Atomically moves <paramref name="source"/> over <paramref name="destination"/>.</summary>
        public static void ReplaceFile(string source, string destination)
        {
            if (File.Exists(destination))
            {
                try
                {
                    File.Replace(source, destination, null, true);
                    return;
                }
                catch (PlatformNotSupportedException) { }
                catch (IOException) { }
                File.Copy(source, destination, true);
                File.Delete(source);
                return;
            }
            File.Move(source, destination);
        }
    }
}
