using System;
using System.Collections.Generic;

namespace Umc2.IntakeServer
{
    /// <summary>
    /// Internet-safe port: serves ONLY the patient form and the submit endpoint.
    /// Cloudflare Tunnel / reverse proxies must point here, never at the staff port.
    /// </summary>
    internal sealed class PublicEndpoints
    {
        public const string FormVersion = "2026.09";
        private readonly ServerHost host;

        public PublicEndpoints(ServerHost host)
        {
            this.host = host;
        }

        public void Handle(HttpCall call)
        {
            if (!call.Path.StartsWith("/api/", StringComparison.Ordinal))
            {
                StaticServer.Serve(host.Files, call, "public");
                return;
            }

            if (call.Method == "GET" && call.Path == "/api/public/health")
            {
                call.WriteJson(200, new Dictionary<string, object> { { "ok", true } });
                return;
            }
            if (call.Method == "GET" && call.Path == "/api/public/config")
            {
                call.WriteJson(200, host.Config.Read(c => new Dictionary<string, object>
                {
                    { "hospitalName", c.HospitalName },
                    { "departmentName", c.DepartmentName },
                    { "formVersion", FormVersion },
                    { "retentionDays", c.RetentionDaysPending }
                }));
                return;
            }
            if (call.Method == "POST" && call.Path == "/api/public/intakes")
            {
                Submit(call);
                return;
            }
            throw new ApiException(404, "not_found", "Không tìm thấy.");
        }

        private void Submit(HttpCall call)
        {
            var perIpLimit = host.Config.Read(c => c.PublicSubmitLimitPerIp);
            var maxPending = host.Config.Read(c => c.MaxPending);
            if (!host.Limiter.Allow("submit:" + call.ClientIp, perIpLimit, TimeSpan.FromMinutes(10)))
                throw new ApiException(429, "rate_limited", "Bạn đã gửi quá nhiều tờ khai. Vui lòng thử lại sau ít phút hoặc liên hệ quầy tiếp nhận.");
            if (!host.Limiter.Allow("submit:all", 120, TimeSpan.FromMinutes(1)) || host.Store.CountByStatus(IntakeStatus.Pending) >= maxPending)
                throw new ApiException(503, "busy", "Hệ thống đang quá tải. Vui lòng khai trực tiếp tại quầy tiếp nhận.");

            var body = call.ReadJson(IntakeValidator.MaxPublicBody);
            var record = IntakeValidator.FromPublic(body, call.ViaTunnel ? "internet" : "lan");
            var saved = host.Store.Add(record, call.ViaTunnel ? "patient-internet" : "patient-lan");
            Logs.Audit("patient", "submit", saved.Code, call.ClientIp);
            host.NotifyChanged();
            call.WriteJson(201, new Dictionary<string, object>
            {
                { "code", saved.Code },
                { "createdAt", saved.CreatedAt },
                { "hospitalName", host.Config.Read(c => c.HospitalName) }
            });
        }
    }

    internal static class StaticServer
    {
        public static void Serve(StaticFiles files, HttpCall call, string area)
        {
            if (call.Method != "GET" && call.Method != "HEAD")
                throw new ApiException(405, "method_not_allowed", "Phương thức không được hỗ trợ.");
            string fileName;
            if (call.Path == "/" || call.Path == "/index.html") fileName = "index.html";
            else if (area == "staff" && call.Path == "/print") fileName = "print.html";
            else if (call.Path == "/favicon.ico")
            {
                call.Redirect("/assets/favicon.svg");
                return;
            }
            else if (call.Path.StartsWith("/assets/", StringComparison.Ordinal)) fileName = call.Path.Substring("/assets/".Length);
            else
            {
                call.WriteBytes(404, "text/plain; charset=utf-8", System.Text.Encoding.UTF8.GetBytes("404 - Không tìm thấy trang."), false, false);
                return;
            }
            var bytes = files.Get(area, fileName);
            if (bytes == null)
            {
                call.WriteBytes(404, "text/plain; charset=utf-8", System.Text.Encoding.UTF8.GetBytes("404 - Không tìm thấy tệp."), false, false);
                return;
            }
            var isHtml = fileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase);
            call.WriteBytes(200, StaticFiles.ContentType(fileName), bytes, !isHtml, StaticFiles.IsCompressible(fileName));
        }
    }
}
