using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Umc2.IntakeServer
{
    internal static class Passwords
    {
        public const int DefaultIterations = 120000;
        public const int MinLength = 8;

        public static void Hash(string password, out string salt, out string hash, int iterations)
        {
            var saltBytes = Tokens.RandomBytes(16);
            salt = Convert.ToBase64String(saltBytes);
            hash = Convert.ToBase64String(Derive(password, saltBytes, iterations));
        }

        public static bool Verify(string password, StaffUser user)
        {
            if (user == null || string.IsNullOrEmpty(user.Salt) || string.IsNullOrEmpty(user.Hash)) return false;
            byte[] salt, expected;
            try
            {
                salt = Convert.FromBase64String(user.Salt);
                expected = Convert.FromBase64String(user.Hash);
            }
            catch (FormatException) { return false; }
            var actual = Derive(password ?? string.Empty, salt, Math.Max(1000, user.Iterations));
            return Tokens.FixedTimeEquals(actual, expected);
        }

        public static string ValidateStrength(string password)
        {
            if (string.IsNullOrEmpty(password) || password.Length < MinLength)
                return "Mật khẩu cần tối thiểu " + MinLength + " ký tự.";
            if (password.Length > 128) return "Mật khẩu quá dài.";
            if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
                return "Mật khẩu cần có cả chữ và số.";
            return null;
        }

        private static byte[] Derive(string password, byte[] salt, int iterations)
        {
            // .NET Framework 4.0 only offers PBKDF2-HMAC-SHA1; the high iteration count compensates.
            using (var kdf = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(password), salt, iterations))
                return kdf.GetBytes(32);
        }
    }

    internal static class Tokens
    {
        private static readonly RandomNumberGenerator Rng = new RNGCryptoServiceProvider();
        private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

        public static byte[] RandomBytes(int count)
        {
            var bytes = new byte[count];
            lock (Rng) Rng.GetBytes(bytes);
            return bytes;
        }

        public static string NewToken(int bytes)
        {
            return Convert.ToBase64String(RandomBytes(bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static string NewId()
        {
            var bytes = RandomBytes(12);
            var sb = new StringBuilder(24);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        /// <summary>Human friendly code like K7P-29Q (no 0/O/1/I/L).</summary>
        public static string NewShortCode()
        {
            var bytes = RandomBytes(6);
            var chars = new char[7];
            for (int i = 0, j = 0; i < 7; i++)
            {
                if (i == 3) { chars[i] = '-'; continue; }
                chars[i] = CodeAlphabet[bytes[j++] % CodeAlphabet.Length];
            }
            return new string(chars);
        }

        public static string NewDigits(int length)
        {
            var bytes = RandomBytes(length * 2);
            var sb = new StringBuilder(length);
            for (var i = 0; i < length; i++)
                sb.Append((char)('0' + (BitConverter.ToUInt16(bytes, i * 2) % 10)));
            return sb.ToString();
        }

        public static string Sha256Hex(string value)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                var sb = new StringBuilder(64);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        public static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            var diff = 0;
            for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        public static bool FixedTimeEquals(string a, string b)
        {
            return FixedTimeEquals(Encoding.UTF8.GetBytes(a ?? string.Empty), Encoding.UTF8.GetBytes(b ?? " "));
        }
    }

    internal sealed class StaffSession
    {
        public string Token { get; set; }
        public string Username { get; set; }
        public string DisplayName { get; set; }
        public string Role { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }
        public bool MustChangePassword { get; set; }
        public bool IsAdmin { get { return Role == Roles.Admin; } }
    }

    internal sealed class SessionManager
    {
        private readonly Dictionary<string, StaffSession> sessions = new Dictionary<string, StaffSession>(StringComparer.Ordinal);
        private readonly TimeSpan absoluteLifetime = TimeSpan.FromHours(12);
        private readonly TimeSpan idleLifetime = TimeSpan.FromHours(2);

        public StaffSession Create(StaffUser user)
        {
            var session = new StaffSession
            {
                Token = Tokens.NewToken(32),
                Username = user.Username,
                DisplayName = string.IsNullOrEmpty(user.DisplayName) ? user.Username : user.DisplayName,
                Role = user.Role,
                MustChangePassword = user.MustChangePassword,
                CreatedUtc = DateTime.UtcNow,
                LastSeenUtc = DateTime.UtcNow
            };
            lock (sessions)
            {
                Purge();
                sessions[session.Token] = session;
            }
            return session;
        }

        public StaffSession Get(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            lock (sessions)
            {
                StaffSession session;
                if (!sessions.TryGetValue(token, out session)) return null;
                var now = DateTime.UtcNow;
                if (now - session.CreatedUtc > absoluteLifetime || now - session.LastSeenUtc > idleLifetime)
                {
                    sessions.Remove(token);
                    return null;
                }
                session.LastSeenUtc = now;
                return session;
            }
        }

        public void Remove(string token)
        {
            if (string.IsNullOrEmpty(token)) return;
            lock (sessions) sessions.Remove(token);
        }

        public void RemoveUser(string username)
        {
            lock (sessions)
            {
                var keys = sessions.Where(p => string.Equals(p.Value.Username, username, StringComparison.OrdinalIgnoreCase))
                    .Select(p => p.Key).ToList();
                foreach (var key in keys) sessions.Remove(key);
            }
        }

        public void UpdateUser(StaffUser user)
        {
            lock (sessions)
            {
                foreach (var session in sessions.Values.Where(s => string.Equals(s.Username, user.Username, StringComparison.OrdinalIgnoreCase)))
                {
                    session.Role = user.Role;
                    session.DisplayName = string.IsNullOrEmpty(user.DisplayName) ? user.Username : user.DisplayName;
                    session.MustChangePassword = user.MustChangePassword;
                }
            }
        }

        private void Purge()
        {
            var now = DateTime.UtcNow;
            var expired = sessions.Where(p => now - p.Value.CreatedUtc > absoluteLifetime || now - p.Value.LastSeenUtc > idleLifetime)
                .Select(p => p.Key).ToList();
            foreach (var key in expired) sessions.Remove(key);
        }
    }

    /// <summary>Sliding-window limiter keyed by caller (IP, username...). In-memory only.</summary>
    internal sealed class RateLimiter
    {
        private readonly Dictionary<string, Queue<DateTime>> hits = new Dictionary<string, Queue<DateTime>>(StringComparer.OrdinalIgnoreCase);
        private DateTime lastSweep = DateTime.UtcNow;

        /// <summary>Records a hit and returns false when the caller is over the limit.</summary>
        public bool Allow(string key, int limit, TimeSpan window)
        {
            return Check(key, limit, window, true);
        }

        /// <summary>Checks without recording (used for failure-only counters such as logins).</summary>
        public bool IsBlocked(string key, int limit, TimeSpan window)
        {
            return !Check(key, limit, window, false);
        }

        public void Record(string key)
        {
            lock (hits)
            {
                Queue<DateTime> queue;
                if (!hits.TryGetValue(key, out queue))
                {
                    queue = new Queue<DateTime>();
                    hits[key] = queue;
                }
                queue.Enqueue(DateTime.UtcNow);
            }
        }

        public void Reset(string key)
        {
            lock (hits) hits.Remove(key);
        }

        private bool Check(string key, int limit, TimeSpan window, bool record)
        {
            var now = DateTime.UtcNow;
            lock (hits)
            {
                if (now - lastSweep > TimeSpan.FromMinutes(10))
                {
                    var stale = hits.Where(p => p.Value.Count == 0 || now - p.Value.Last() > TimeSpan.FromHours(2)).Select(p => p.Key).ToList();
                    foreach (var k in stale) hits.Remove(k);
                    lastSweep = now;
                }
                Queue<DateTime> queue;
                if (!hits.TryGetValue(key, out queue))
                {
                    queue = new Queue<DateTime>();
                    hits[key] = queue;
                }
                while (queue.Count > 0 && now - queue.Peek() > window) queue.Dequeue();
                if (queue.Count >= limit) return false;
                if (record) queue.Enqueue(now);
                return true;
            }
        }
    }

    /// <summary>Short-lived 6-digit codes used once to pair a doctor's PC (HIS assistant) with the server.</summary>
    internal sealed class PairingCodes
    {
        private sealed class Entry
        {
            public string Code;
            public DateTime ExpiresUtc;
            public string CreatedBy;
        }

        private readonly List<Entry> entries = new List<Entry>();

        public string Create(string createdBy, TimeSpan lifetime, out DateTime expiresUtc)
        {
            lock (entries)
            {
                entries.RemoveAll(e => e.ExpiresUtc < DateTime.UtcNow);
                string code;
                do { code = Tokens.NewDigits(6); } while (entries.Any(e => e.Code == code));
                var entry = new Entry { Code = code, ExpiresUtc = DateTime.UtcNow.Add(lifetime), CreatedBy = createdBy };
                entries.Add(entry);
                expiresUtc = entry.ExpiresUtc;
                return code;
            }
        }

        public bool TryConsume(string code, out string createdBy)
        {
            createdBy = null;
            code = (code ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty);
            lock (entries)
            {
                entries.RemoveAll(e => e.ExpiresUtc < DateTime.UtcNow);
                var match = entries.FirstOrDefault(e => Tokens.FixedTimeEquals(e.Code, code));
                if (match == null) return false;
                entries.Remove(match);
                createdBy = match.CreatedBy;
                return true;
            }
        }
    }

    internal static class NetUtil
    {
        /// <summary>True for loopback, RFC1918, link-local, CGNAT (100.64/10) and IPv6 ULA/link-local.</summary>
        public static bool IsPrivateOrLoopback(IPAddress address)
        {
            if (address == null) return false;
            if (IPAddress.IsLoopback(address)) return true;
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal) return true;
                var bytes6 = address.GetAddressBytes();
                if ((bytes6[0] & 0xFE) == 0xFC) return true; // fc00::/7
                if (IsIpv4Mapped(bytes6))
                    return IsPrivateOrLoopback(new IPAddress(new[] { bytes6[12], bytes6[13], bytes6[14], bytes6[15] }));
                return false;
            }
            var b = address.GetAddressBytes();
            if (b[0] == 10) return true;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
            if (b[0] == 192 && b[1] == 168) return true;
            if (b[0] == 169 && b[1] == 254) return true;
            if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return true;
            return false;
        }

        public static bool IsLoopback(IPAddress address)
        {
            if (address == null) return false;
            if (IPAddress.IsLoopback(address)) return true;
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                var bytes = address.GetAddressBytes();
                if (IsIpv4Mapped(bytes)) return bytes[12] == 127;
            }
            return false;
        }

        private static bool IsIpv4Mapped(byte[] bytes)
        {
            if (bytes.Length != 16) return false;
            for (var i = 0; i < 10; i++) if (bytes[i] != 0) return false;
            return bytes[10] == 0xFF && bytes[11] == 0xFF;
        }
    }
}
