using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace HisAdmissionAssistant
{
    /// <summary>
    /// Pairing + UI preferences for this doctor's PC. Encrypted with Windows DPAPI for the current Windows user
    /// (%LOCALAPPDATA%\UMC2\HisAdmissionAssistant\connection.dat). Contains no patient data.
    /// The device token can be revoked at any time from the intake server dashboard.
    /// </summary>
    public sealed class ConnectionSettings
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("UMC2-HisAdmissionAssistant-connection-v1");

        public ConnectionSettings()
        {
            ServerUrl = DeviceId = DeviceName = Token = HospitalName = string.Empty;
            AutoDetect = true;
            QuickPanel = true;
            QuickPanelX = -1;
            QuickPanelY = -1;
        }

        public string ServerUrl { get; set; }
        public string DeviceId { get; set; }
        public string DeviceName { get; set; }
        public string Token { get; set; }
        public string HospitalName { get; set; }
        public bool AutoDetect { get; set; }
        public bool QuickPanel { get; set; }
        public int QuickPanelX { get; set; }
        public int QuickPanelY { get; set; }

        [ScriptIgnore]
        public bool IsPaired
        {
            get { return !string.IsNullOrWhiteSpace(ServerUrl) && !string.IsNullOrWhiteSpace(Token); }
        }

        public static string FilePath
        {
            get
            {
                return Path.Combine(Path.Combine(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UMC2"), "HisAdmissionAssistant"), "connection.dat");
            }
        }

        public static ConnectionSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new ConnectionSettings();
                var plain = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser);
                var loaded = new JavaScriptSerializer().Deserialize<ConnectionSettings>(Encoding.UTF8.GetString(plain));
                return loaded ?? new ConnectionSettings();
            }
            catch (CryptographicException)
            {
                // Copied from another user/PC: start unpaired instead of failing.
                return new ConnectionSettings();
            }
            catch (IOException)
            {
                return new ConnectionSettings();
            }
            catch (ArgumentException)
            {
                return new ConnectionSettings();
            }
            catch (InvalidOperationException)
            {
                return new ConnectionSettings();
            }
        }

        public void Save()
        {
            var directory = Path.GetDirectoryName(FilePath);
            Directory.CreateDirectory(directory);
            var plain = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(this));
            var cipher = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            Array.Clear(plain, 0, plain.Length);
            var tmp = FilePath + ".tmp";
            File.WriteAllBytes(tmp, cipher);
            if (File.Exists(FilePath)) File.Delete(FilePath);
            File.Move(tmp, FilePath);
        }

        public void ForgetPairing()
        {
            Token = string.Empty;
            DeviceId = string.Empty;
        }
    }
}
