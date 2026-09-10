using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace Umc2.IntakeServer
{
    internal enum PortKind
    {
        Public,
        Staff
    }

    /// <summary>Thin HttpListener wrapper: one accept thread, requests handled on the thread pool.</summary>
    internal sealed class HttpHost : IDisposable
    {
        private readonly string name;
        private readonly int port;
        private readonly string bindHost;
        private readonly Action<HttpCall> handler;
        private readonly PortKind kind;
        private HttpListener listener;
        private Thread acceptThread;
        private volatile bool running;

        public HttpHost(string name, PortKind kind, string bindHost, int port, Action<HttpCall> handler)
        {
            this.name = name;
            this.kind = kind;
            this.bindHost = string.IsNullOrWhiteSpace(bindHost) ? "+" : bindHost.Trim();
            this.port = port;
            this.handler = handler;
        }

        /// <summary>True when binding to all interfaces was refused (no urlacl) and the server fell back to localhost only.</summary>
        public bool LocalOnly { get; private set; }

        public int Port { get { return port; } }

        public void Start()
        {
            running = true;
            try
            {
                listener = CreateAndStart(bindHost);
                LocalOnly = bindHost == "localhost" || bindHost == "127.0.0.1";
            }
            catch (HttpListenerException ex)
            {
                if (bindHost == "localhost") throw;
                Logs.Warn(name + ": không mở được http://" + bindHost + ":" + port + "/ (" + ex.Message +
                    "). Chỉ phục vụ trên máy này. Chạy install-server.ps1 bằng quyền Administrator để mở cho mạng LAN.");
                listener = CreateAndStart("localhost");
                LocalOnly = true;
            }
            acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = name + "-accept" };
            acceptThread.Start();
            Logs.Info(name + " đang lắng nghe cổng " + port + (LocalOnly ? " (chỉ localhost)" : " (mọi giao diện mạng)"));
        }

        private HttpListener CreateAndStart(string host)
        {
            var result = new HttpListener();
            result.Prefixes.Add("http://" + host + ":" + port + "/");
            result.IgnoreWriteExceptions = true;
            result.Start();
            return result;
        }

        private void AcceptLoop()
        {
            while (running)
            {
                HttpListenerContext context;
                try
                {
                    context = listener.GetContext();
                }
                catch (HttpListenerException)
                {
                    if (!running) break;
                    continue;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (InvalidOperationException)
                {
                    break;
                }
                ThreadPool.QueueUserWorkItem(Process, context);
            }
        }

        private void Process(object state)
        {
            var context = (HttpListenerContext)state;
            HttpCall call = null;
            try
            {
                call = new HttpCall(context, kind);
                handler(call);
            }
            catch (ApiException ex)
            {
                if (call != null) call.WriteError(ex.Status, ex.Code, ex.Message);
            }
            catch (Exception ex)
            {
                Logs.Error(name + " lỗi xử lý " + (call == null ? "?" : call.Method + " " + call.Path), ex);
                if (call != null) call.WriteError(500, "server_error", "Máy chủ gặp lỗi. Vui lòng thử lại.");
            }
            finally
            {
                try { context.Response.Close(); }
                catch (Exception) { }
            }
        }

        public void Dispose()
        {
            running = false;
            if (listener != null)
            {
                try { listener.Stop(); }
                catch (Exception) { }
                try { listener.Close(); }
                catch (Exception) { }
            }
        }
    }

    /// <summary>Request/response helpers for one HTTP exchange.</summary>
    internal sealed class HttpCall
    {
        private static readonly string[] ProxyHeaders = { "CF-Connecting-IP", "CF-Ray", "CF-Visitor", "X-Forwarded-For", "Forwarded", "X-Real-IP" };
        private readonly HttpListenerContext context;
        private bool responded;

        public HttpCall(HttpListenerContext context, PortKind kind)
        {
            this.context = context;
            Kind = kind;
            Method = context.Request.HttpMethod.ToUpperInvariant();
            var rawPath = context.Request.Url.AbsolutePath;
            Path = NormalizePath(Uri.UnescapeDataString(rawPath));
            Query = context.Request.QueryString;
            RemoteIp = context.Request.RemoteEndPoint == null ? IPAddress.None : context.Request.RemoteEndPoint.Address;
            HasProxyHeaders = ProxyHeaders.Any(h => !string.IsNullOrEmpty(context.Request.Headers[h]));
            var forwarded = context.Request.Headers["CF-Connecting-IP"];
            // Only trust the tunnel header when the TCP peer is this machine (cloudflared runs locally).
            ClientIp = NetUtil.IsLoopback(RemoteIp) && !string.IsNullOrEmpty(forwarded) ? forwarded.Trim() : RemoteIp.ToString();
            ViaTunnel = NetUtil.IsLoopback(RemoteIp) && !string.IsNullOrEmpty(forwarded);
        }

        public PortKind Kind { get; private set; }
        public string Method { get; private set; }
        public string Path { get; private set; }
        public NameValueCollection Query { get; private set; }
        public IPAddress RemoteIp { get; private set; }
        public string ClientIp { get; private set; }
        public bool HasProxyHeaders { get; private set; }
        public bool ViaTunnel { get; private set; }
        public StaffSession Session { get; set; }
        public AgentDevice Device { get; set; }
        public bool IsLocalMachine { get { return NetUtil.IsLoopback(RemoteIp) && !HasProxyHeaders; } }
        public bool IsHttps { get { return context.Request.IsSecureConnection || string.Equals(Header("X-Forwarded-Proto"), "https", StringComparison.OrdinalIgnoreCase); } }

        public string Header(string headerName)
        {
            return context.Request.Headers[headerName] ?? string.Empty;
        }

        public string Cookie(string cookieName)
        {
            var cookie = context.Request.Cookies[cookieName];
            return cookie == null ? string.Empty : cookie.Value;
        }

        public string BearerToken()
        {
            var auth = Header("Authorization");
            return auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? auth.Substring(7).Trim() : string.Empty;
        }

        public string ReadBody(int maxBytes)
        {
            var request = context.Request;
            if (!request.HasEntityBody) return string.Empty;
            if (request.ContentLength64 > maxBytes) throw new ApiException(413, "too_large", "Dữ liệu gửi lên quá lớn.");
            using (var input = request.InputStream)
            using (var buffer = new MemoryStream())
            {
                var chunk = new byte[8192];
                int read;
                while ((read = input.Read(chunk, 0, chunk.Length)) > 0)
                {
                    buffer.Write(chunk, 0, read);
                    if (buffer.Length > maxBytes) throw new ApiException(413, "too_large", "Dữ liệu gửi lên quá lớn.");
                }
                return Encoding.UTF8.GetString(buffer.ToArray());
            }
        }

        public Dictionary<string, object> ReadJson(int maxBytes)
        {
            var contentType = Header("Content-Type");
            if (contentType.Length > 0 && contentType.IndexOf("json", StringComparison.OrdinalIgnoreCase) < 0)
                throw new ApiException(415, "unsupported_media_type", "Chỉ nhận dữ liệu JSON.");
            var body = ReadBody(maxBytes);
            return string.IsNullOrWhiteSpace(body) ? new Dictionary<string, object>() : Json.ParseObject(body);
        }

        public void WriteJson(int status, object payload)
        {
            var bytes = Encoding.UTF8.GetBytes(Json.Serialize(payload));
            WriteBytes(status, "application/json; charset=utf-8", bytes, false, true);
        }

        public void WriteError(int status, string code, string message)
        {
            if (responded) return;
            WriteJson(status, new Dictionary<string, object> { { "error", code }, { "message", message } });
        }

        public void WriteBytes(int status, string contentType, byte[] bytes, bool allowCache, bool compressible)
        {
            if (responded) return;
            responded = true;
            var response = context.Response;
            try
            {
                response.StatusCode = status;
                response.ContentType = contentType;
                ApplySecurityHeaders(response, contentType);
                response.Headers["Cache-Control"] = allowCache ? "public, max-age=300" : "no-store";
                if (!allowCache) response.Headers["Pragma"] = "no-cache";
                if (compressible && bytes.Length > 1400 && Header("Accept-Encoding").IndexOf("gzip", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    bytes = Gzip(bytes);
                    response.Headers["Content-Encoding"] = "gzip";
                    response.Headers["Vary"] = "Accept-Encoding";
                }
                if (Method == "HEAD")
                {
                    response.ContentLength64 = bytes.Length;
                    return;
                }
                response.ContentLength64 = bytes.Length;
                response.OutputStream.Write(bytes, 0, bytes.Length);
            }
            catch (HttpListenerException)
            {
                // Client went away.
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void WriteNoContent()
        {
            if (responded) return;
            responded = true;
            context.Response.StatusCode = 204;
            ApplySecurityHeaders(context.Response, string.Empty);
            context.Response.Headers["Cache-Control"] = "no-store";
        }

        public void Redirect(string location)
        {
            if (responded) return;
            responded = true;
            context.Response.StatusCode = 302;
            context.Response.Headers["Location"] = location;
            ApplySecurityHeaders(context.Response, string.Empty);
        }

        public void SetCookie(string cookieName, string value, TimeSpan? maxAge)
        {
            var sb = new StringBuilder();
            sb.Append(cookieName).Append('=').Append(value).Append("; Path=/; HttpOnly; SameSite=Strict");
            if (maxAge.HasValue) sb.Append("; Max-Age=").Append((int)maxAge.Value.TotalSeconds);
            if (IsHttps) sb.Append("; Secure");
            context.Response.Headers.Add("Set-Cookie", sb.ToString());
        }

        private static void ApplySecurityHeaders(HttpListenerResponse response, string contentType)
        {
            response.Headers["X-Content-Type-Options"] = "nosniff";
            response.Headers["Referrer-Policy"] = "no-referrer";
            response.Headers["X-Frame-Options"] = "DENY";
            response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
            response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
            response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            if (contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
            {
                response.Headers["Content-Security-Policy"] =
                    "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; " +
                    "connect-src 'self'; object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'";
            }
        }

        private static byte[] Gzip(byte[] bytes)
        {
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionMode.Compress, true))
                    gzip.Write(bytes, 0, bytes.Length);
                return output.ToArray();
            }
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "/";
            path = path.Replace('\\', '/');
            while (path.Contains("//")) path = path.Replace("//", "/");
            if (path.Split('/').Any(segment => segment == ".." || segment == ".")) return "/__invalid__";
            if (path.Length > 1 && path.EndsWith("/")) path = path.TrimEnd('/');
            return path.Length == 0 ? "/" : path;
        }
    }
}
