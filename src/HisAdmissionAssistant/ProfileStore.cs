using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace HisAdmissionAssistant
{
    public sealed class ProfileStore
    {
        private readonly string bundledDirectory;
        private readonly string userDirectory;

        public ProfileStore()
        {
            bundledDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles");
            userDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "UMC2",
                "HisAdmissionAssistant",
                "Profiles");
        }

        public string UserDirectory { get { return userDirectory; } }

        public IList<AutomationProfile> LoadAll()
        {
            Directory.CreateDirectory(userDirectory);
            var profiles = new List<AutomationProfile>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in EnumerateProfilePaths())
            {
                try
                {
                    var profile = Load(path);
                    if (seenNames.Add(profile.Name)) profiles.Add(profile);
                }
                catch
                {
                    // A malformed profile must not prevent the operator from opening the app.
                }
            }
            return profiles
                .OrderBy(p => ProfileOrder(p.Name))
                .ThenBy(p => p.Name)
                .ToList();
        }

        public AutomationProfile Load(string path)
        {
            var serializer = new XmlSerializer(typeof(AutomationProfile));
            using (var stream = File.OpenRead(path))
                return (AutomationProfile)serializer.Deserialize(stream);
        }

        public string SaveUserProfile(AutomationProfile profile)
        {
            Directory.CreateDirectory(userDirectory);
            var safeName = MakeSafeFileName(profile.Name);
            if (string.IsNullOrWhiteSpace(safeName)) safeName = "profile";
            var path = Path.Combine(userDirectory, safeName + ".xml");
            var serializer = new XmlSerializer(typeof(AutomationProfile));
            var settings = new System.Xml.XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
            using (var writer = System.Xml.XmlWriter.Create(path, settings))
                serializer.Serialize(writer, profile);
            return path;
        }

        private IEnumerable<string> EnumerateProfilePaths()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(userDirectory))
            {
                foreach (var path in Directory.GetFiles(userDirectory, "*.xml"))
                {
                    seen.Add(Path.GetFileName(path));
                    yield return path;
                }
            }
            if (Directory.Exists(bundledDirectory))
            {
                foreach (var path in Directory.GetFiles(bundledDirectory, "*.xml"))
                    if (!seen.Contains(Path.GetFileName(path))) yield return path;
            }
        }

        private static string MakeSafeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string((value ?? string.Empty).Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }

        private static int ProfileOrder(string name)
        {
            name = name ?? string.Empty;
            if (name.IndexOf("Phiếu khám vào viện", StringComparison.OrdinalIgnoreCase) >= 0) return 0;
            if (name.IndexOf("Khám bệnh và chỉ định", StringComparison.OrdinalIgnoreCase) >= 0) return 1;
            if (name.IndexOf("Chọn phòng", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            if (name.StartsWith("Kiểm thử", StringComparison.OrdinalIgnoreCase)) return 9;
            return 5;
        }
    }
}
