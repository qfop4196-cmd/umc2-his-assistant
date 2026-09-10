using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Web.Script.Serialization;

namespace HisAdmissionAssistant
{
    /// <summary>One approved patient intake as seen by a doctor's PC.</summary>
    public sealed class IntakeMatch
    {
        public IntakeMatch()
        {
            Id = Code = Status = FullName = Gender = HisPatientId = ChiefComplaint = CreatedAt = ApprovedAt = ApprovedBy = ClaimedBy = ClaimedById = MatchReason = string.Empty;
            Fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public string Id { get; set; }
        public string Code { get; set; }
        public string Status { get; set; }
        public string FullName { get; set; }
        public int BirthYear { get; set; }
        public string Gender { get; set; }
        public string HisPatientId { get; set; }
        public string ChiefComplaint { get; set; }
        public string CreatedAt { get; set; }
        public string ApprovedAt { get; set; }
        public string ApprovedBy { get; set; }
        public string ClaimedBy { get; set; }
        public string ClaimedById { get; set; }
        public int Score { get; set; }
        public string MatchReason { get; set; }
        public IDictionary<string, string> Fields { get; set; }

        public string GenderText
        {
            get { return Gender == "male" ? "Nam" : Gender == "female" ? "Nữ" : Gender == "other" ? "Khác" : string.Empty; }
        }

        public string ApprovedClock
        {
            get
            {
                DateTimeOffset time;
                return DateTimeOffset.TryParse(ApprovedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out time)
                    ? time.ToLocalTime().ToString("HH:mm dd/MM") : string.Empty;
            }
        }
    }

    public sealed class PairResult
    {
        public string DeviceId { get; set; }
        public string DeviceName { get; set; }
        public string Token { get; set; }
        public string HospitalName { get; set; }
    }

    public sealed class DiscoveredServer
    {
        public string Url { get; set; }
        public string HospitalName { get; set; }
        public string Version { get; set; }

        public override string ToString()
        {
            return Url + (string.IsNullOrEmpty(HospitalName) ? string.Empty : "  —  " + HospitalName);
        }
    }

    public sealed class IntakeApiException : Exception
    {
        public IntakeApiException(int status, string code, string message) : base(message)
        {
            Status = status;
            Code = code ?? string.Empty;
        }

        public int Status { get; private set; }
        public string Code { get; private set; }
        public bool IsUnauthorized { get { return Status == 401; } }
    }

    /// <summary>
    /// Client for the LAN intake server agent API (UMC2 Intake Server). Uses HttpWebRequest so it runs on
    /// .NET Framework 4.0 without extra packages. No patient data is written to disk.
    /// </summary>
    public sealed class IntakeClient
    {
        public const int DiscoveryPort = 47810;
        private const string DiscoveryProbe = "UMC2-INTAKE-DISCOVER v1";
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };

        public string BaseUrl { get; set; }
        public string Token { get; set; }

        public bool IsConfigured
        {
            get { return !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Token); }
        }

        public static string NormalizeUrl(string url)
        {
            var value = (url ?? string.Empty).Trim();
            if (value.Length == 0) return value;
            if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                value = "http://" + value;
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri)) throw new InvalidOperationException("Địa chỉ máy chủ không hợp lệ: " + url);
            if (uri.IsDefaultPort && uri.Scheme == "http" && value.IndexOf(":" + uri.Port, StringComparison.Ordinal) < 0 && uri.Host.Length > 0)
                value = uri.Scheme + "://" + uri.Host + ":8080";
            else
                value = uri.Scheme + "://" + uri.Authority;
            return value.TrimEnd('/');
        }

        public PairResult Pair(string baseUrl, string code, string deviceName)
        {
            var url = NormalizeUrl(baseUrl);
            var response = Send(url, "POST", "/api/agent/pair", new Dictionary<string, object>
            {
                { "code", code },
                { "deviceName", deviceName },
                { "machineName", Environment.MachineName }
            }, null);
            return new PairResult
            {
                DeviceId = Str(response, "deviceId"),
                DeviceName = Str(response, "deviceName"),
                Token = Str(response, "token"),
                HospitalName = Str(response, "hospitalName")
            };
        }

        public IDictionary<string, object> Ping()
        {
            return Send(BaseUrl, "GET", "/api/agent/ping", null, Token);
        }

        public IList<IntakeMatch> Match(string patientId, string patientName, string birthYear)
        {
            var response = Send(BaseUrl, "POST", "/api/agent/intakes/match", new Dictionary<string, object>
            {
                { "patientId", patientId ?? string.Empty },
                { "patientName", patientName ?? string.Empty },
                { "birthYear", birthYear ?? string.Empty }
            }, Token);
            return List(response, "matches");
        }

        public IList<IntakeMatch> Recent()
        {
            return List(Send(BaseUrl, "GET", "/api/agent/intakes", null, Token), "items");
        }

        public IntakeMatch Get(string id)
        {
            return ToMatch(Send(BaseUrl, "GET", "/api/agent/intakes/" + Uri.EscapeDataString(id), null, Token));
        }

        public IntakeMatch Claim(string id, string observedPatientId, bool force)
        {
            return ToMatch(Send(BaseUrl, "POST", "/api/agent/intakes/" + Uri.EscapeDataString(id) + "/claim", new Dictionary<string, object>
            {
                { "hisPatientId", observedPatientId ?? string.Empty },
                { "force", force }
            }, Token));
        }

        public void Release(string id)
        {
            Send(BaseUrl, "POST", "/api/agent/intakes/" + Uri.EscapeDataString(id) + "/release", new Dictionary<string, object>(), Token);
        }

        public void Complete(string id, string result, string summary, string observedPatientId, int filledCount)
        {
            Send(BaseUrl, "POST", "/api/agent/intakes/" + Uri.EscapeDataString(id) + "/complete", new Dictionary<string, object>
            {
                { "result", result },
                { "summary", summary ?? string.Empty },
                { "hisPatientId", observedPatientId ?? string.Empty },
                { "filledCount", filledCount }
            }, Token);
        }

        /// <summary>Finds intake servers on the local network segment by UDP broadcast (no admin rights needed).</summary>
        public static IList<DiscoveredServer> Discover(int timeoutMs)
        {
            var found = new Dictionary<string, DiscoveredServer>(StringComparer.OrdinalIgnoreCase);
            var serializer = new JavaScriptSerializer();
            using (var udp = new UdpClient())
            {
                udp.EnableBroadcast = true;
                udp.Client.ReceiveTimeout = 400;
                var probe = Encoding.ASCII.GetBytes(DiscoveryProbe);
                foreach (var target in BroadcastTargets())
                {
                    try { udp.Send(probe, probe.Length, new IPEndPoint(target, DiscoveryPort)); }
                    catch (SocketException) { }
                }
                var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                while (DateTime.UtcNow < deadline)
                {
                    try
                    {
                        var remote = new IPEndPoint(IPAddress.Any, 0);
                        var data = udp.Receive(ref remote);
                        var reply = serializer.DeserializeObject(Encoding.UTF8.GetString(data)) as IDictionary<string, object>;
                        if (reply == null || Str(reply, "service") != "umc2-intake") continue;
                        int port;
                        if (!int.TryParse(Str(reply, "staffPort"), out port)) port = 8080;
                        var url = "http://" + remote.Address + ":" + port;
                        if (!found.ContainsKey(url))
                            found[url] = new DiscoveredServer { Url = url, HospitalName = Str(reply, "hospitalName"), Version = Str(reply, "version") };
                    }
                    catch (SocketException)
                    {
                        // receive timeout slice; keep waiting until the deadline
                    }
                    catch (ArgumentException)
                    {
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
            }
            return found.Values.ToList();
        }

        private static IEnumerable<IPAddress> BroadcastTargets()
        {
            var targets = new List<IPAddress> { IPAddress.Broadcast };
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask == null) continue;
                        var ip = unicast.Address.GetAddressBytes();
                        var mask = unicast.IPv4Mask.GetAddressBytes();
                        if (mask.Length != 4) continue;
                        var broadcast = new byte[4];
                        for (var i = 0; i < 4; i++) broadcast[i] = (byte)(ip[i] | ~mask[i]);
                        var address = new IPAddress(broadcast);
                        if (!targets.Contains(address)) targets.Add(address);
                    }
                }
            }
            catch (NetworkInformationException)
            {
            }
            targets.Add(IPAddress.Loopback);
            return targets;
        }

        private IDictionary<string, object> Send(string baseUrl, string method, string path, object body, string token)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) throw new InvalidOperationException("Chưa cấu hình địa chỉ máy chủ tờ khai.");
            if (token != null && token.Length == 0) throw new IntakeApiException(401, "unauthorized", "Máy này chưa được ghép nối với máy chủ tờ khai.");
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | SecurityProtocolType.Tls;
            var request = (HttpWebRequest)WebRequest.Create(baseUrl.TrimEnd('/') + path);
            request.Method = method;
            request.Accept = "application/json";
            request.Timeout = 8000;
            request.ReadWriteTimeout = 8000;
            request.KeepAlive = true;
            request.UserAgent = "HisAdmissionAssistant/" + typeof(IntakeClient).Assembly.GetName().Version;
            if (IsLanHost(request.RequestUri.Host)) request.Proxy = null;
            if (!string.IsNullOrEmpty(token)) request.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;
            if (body != null)
            {
                var bytes = Encoding.UTF8.GetBytes(json.Serialize(body));
                request.ContentType = "application/json; charset=utf-8";
                request.ContentLength = bytes.Length;
                using (var stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
            }
            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    var text = reader.ReadToEnd();
                    return string.IsNullOrWhiteSpace(text) ? new Dictionary<string, object>() : (json.DeserializeObject(text) as IDictionary<string, object> ?? new Dictionary<string, object>());
                }
            }
            catch (WebException ex)
            {
                var response = ex.Response as HttpWebResponse;
                if (response == null)
                    throw new IntakeApiException(0, "network", "Không kết nối được máy chủ tờ khai (" + ex.Status + "). Kiểm tra mạng LAN hoặc máy chủ.");
                using (response)
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    var text = reader.ReadToEnd();
                    string code = string.Empty, message = "Máy chủ từ chối (HTTP " + (int)response.StatusCode + ").";
                    try
                    {
                        var error = json.DeserializeObject(text) as IDictionary<string, object>;
                        if (error != null)
                        {
                            code = Str(error, "error");
                            if (Str(error, "message").Length > 0) message = Str(error, "message");
                        }
                    }
                    catch (ArgumentException)
                    {
                    }
                    throw new IntakeApiException((int)response.StatusCode, code, message);
                }
            }
        }

        private static bool IsLanHost(string host)
        {
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) || host.IndexOf('.') < 0) return true;
            IPAddress address;
            if (!IPAddress.TryParse(host, out address)) return false;
            var b = address.GetAddressBytes();
            if (b.Length != 4) return IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal;
            return b[0] == 10 || b[0] == 127 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
        }

        private static IList<IntakeMatch> List(IDictionary<string, object> response, string key)
        {
            object value;
            var result = new List<IntakeMatch>();
            if (!response.TryGetValue(key, out value) || value == null) return result;
            var items = value as IEnumerable;
            if (items == null) return result;
            foreach (var item in items)
            {
                var dictionary = item as IDictionary<string, object>;
                if (dictionary != null) result.Add(ToMatch(dictionary));
            }
            return result;
        }

        private static IntakeMatch ToMatch(IDictionary<string, object> source)
        {
            var match = new IntakeMatch
            {
                Id = Str(source, "id"),
                Code = Str(source, "code"),
                Status = Str(source, "status"),
                FullName = Str(source, "fullName"),
                Gender = Str(source, "gender"),
                HisPatientId = Str(source, "hisPatientId"),
                ChiefComplaint = Str(source, "chiefComplaint"),
                CreatedAt = Str(source, "createdAt"),
                ApprovedAt = Str(source, "approvedAt"),
                ApprovedBy = Str(source, "approvedBy"),
                ClaimedBy = Str(source, "claimedBy"),
                ClaimedById = Str(source, "claimedById"),
                MatchReason = Str(source, "matchReason")
            };
            int number;
            if (int.TryParse(Str(source, "birthYear"), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) match.BirthYear = number;
            if (int.TryParse(Str(source, "score"), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) match.Score = number;
            object fieldsObject;
            if (source.TryGetValue("fields", out fieldsObject))
            {
                var fields = fieldsObject as IDictionary<string, object>;
                if (fields != null)
                    foreach (var pair in fields)
                        match.Fields[pair.Key] = pair.Value == null ? string.Empty : Convert.ToString(pair.Value, CultureInfo.InvariantCulture);
            }
            return match;
        }

        private static string Str(IDictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : string.Empty;
        }
    }
}
