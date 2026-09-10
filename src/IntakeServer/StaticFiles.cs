using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Umc2.IntakeServer
{
    /// <summary>
    /// Serves the web UI. Files are embedded in IntakeServer.exe (single-file deployment).
    /// A "wwwroot" folder next to the exe overrides embedded files (e.g. a hospital logo) without recompiling.
    /// Layout: wwwroot/shared/* (both ports), wwwroot/public/* (patient port), wwwroot/staff/* (staff port).
    /// </summary>
    internal sealed class StaticFiles
    {
        private readonly Dictionary<string, byte[]> embedded = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        private readonly string overrideRoot;

        public StaticFiles()
        {
            var assembly = Assembly.GetExecutingAssembly();
            foreach (var resource in assembly.GetManifestResourceNames())
            {
                var key = NormalizeResourceName(resource);
                if (key == null) continue;
                using (var stream = assembly.GetManifestResourceStream(resource))
                using (var memory = new MemoryStream())
                {
                    if (stream == null) continue;
                    stream.CopyTo(memory);
                    embedded[key] = memory.ToArray();
                }
            }
            overrideRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");
        }

        public int EmbeddedCount { get { return embedded.Count; } }

        /// <summary>Returns the bytes for area ("public" | "staff") and file name, or null.</summary>
        public byte[] Get(string area, string fileName)
        {
            if (string.IsNullOrEmpty(fileName) || fileName.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || fileName.StartsWith(".")) return null;
            return Find(area + "/" + fileName) ?? Find("shared/" + fileName);
        }

        private byte[] Find(string relative)
        {
            if (Directory.Exists(overrideRoot))
            {
                var full = Path.GetFullPath(Path.Combine(overrideRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
                var rootFull = Path.GetFullPath(overrideRoot) + Path.DirectorySeparatorChar;
                if (full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) && File.Exists(full))
                    return File.ReadAllBytes(full);
            }
            byte[] bytes;
            return embedded.TryGetValue(relative, out bytes) ? bytes : null;
        }

        public static string ContentType(string fileName)
        {
            switch ((Path.GetExtension(fileName) ?? string.Empty).ToLowerInvariant())
            {
                case ".html": return "text/html; charset=utf-8";
                case ".css": return "text/css; charset=utf-8";
                case ".js": return "application/javascript; charset=utf-8";
                case ".svg": return "image/svg+xml";
                case ".png": return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".ico": return "image/x-icon";
                case ".webmanifest": return "application/manifest+json";
                case ".woff2": return "font/woff2";
                case ".txt": return "text/plain; charset=utf-8";
                default: return "application/octet-stream";
            }
        }

        public static bool IsCompressible(string fileName)
        {
            var ext = (Path.GetExtension(fileName) ?? string.Empty).ToLowerInvariant();
            return ext == ".html" || ext == ".css" || ext == ".js" || ext == ".svg" || ext == ".txt" || ext == ".webmanifest";
        }

        private static string NormalizeResourceName(string resource)
        {
            // build.ps1 embeds files as "wwwroot/<area>/<file>".
            var name = resource.Replace('\\', '/');
            var index = name.IndexOf("wwwroot/", StringComparison.OrdinalIgnoreCase);
            if (index >= 0) return name.Substring(index + "wwwroot/".Length);
            // Visual Studio/MSBuild default: "Umc2.IntakeServer.wwwroot.<area>.<file.ext>".
            index = name.IndexOf(".wwwroot.", StringComparison.OrdinalIgnoreCase);
            if (index < 0) return null;
            var rest = name.Substring(index + ".wwwroot.".Length);
            var dot = rest.IndexOf('.');
            return dot <= 0 ? null : rest.Substring(0, dot) + "/" + rest.Substring(dot + 1);
        }
    }
}
