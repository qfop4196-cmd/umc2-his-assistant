using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Umc2.IntakeServer
{
    /// <summary>LAN-only port: nurse/admin dashboard API, first-run setup and the HIS assistant (agent) API.</summary>
    internal sealed class StaffEndpoints
    {
        private const string SessionCookie = "umc2_staff";
        private static readonly Regex IntakePath = new Regex("^/api/staff/intakes/([a-f0-9]{24})(/[a-z-]+)?$", RegexOptions.Compiled);
        private static readonly Regex DevicePath = new Regex("^/api/staff/devices/([a-f0-9]{24})/revoke$", RegexOptions.Compiled);
        private static readonly Regex UsernamePattern = new Regex("^[a-z0-9][a-z0-9._-]{2,31}$", RegexOptions.Compiled);
        private readonly ServerHost host;
        private readonly AgentEndpoints agent;

        public StaffEndpoints(ServerHost host)
        {
            this.host = host;
            agent = new AgentEndpoints(host);
        }

        public void Handle(HttpCall call)
        {
            var allowPublic = host.Config.Read(c => c.AllowPublicStaffAccess);
            if (!allowPublic && (call.HasProxyHeaders || !NetUtil.IsPrivateOrLoopback(call.RemoteIp)))
            {
                Logs.Audit("-", "blocked-non-lan", call.Path, call.ClientIp);
                throw new ApiException(403, "lan_only", "Trang này chỉ truy cập được từ mạng nội bộ bệnh viện.");
            }

            if (!call.Path.StartsWith("/api/", StringComparison.Ordinal))
            {
                StaticServer.Serve(host.Files, call, "staff");
                return;
            }
            if (call.Path.StartsWith("/api/agent/", StringComparison.Ordinal))
            {
                agent.Handle(call);
                return;
            }

            if (call.Method != "GET" && call.Method != "HEAD" && call.Header("X-UMC2") != "1")
                throw new ApiException(403, "csrf", "Yêu cầu không hợp lệ.");

            call.Session = host.Sessions.Get(call.Cookie(SessionCookie));

            switch (call.Method + " " + call.Path)
            {
                case "GET /api/staff/bootstrap": Bootstrap(call); return;
                case "POST /api/setup": Setup(call); return;
                case "POST /api/staff/login": Login(call); return;
                case "POST /api/staff/logout": Logout(call); return;
            }

            RequireSession(call);
            if (call.Session.MustChangePassword && call.Path != "/api/staff/password")
                throw new ApiException(403, "must_change_password", "Vui lòng đổi mật khẩu trước khi tiếp tục.");

            switch (call.Method + " " + call.Path)
            {
                case "POST /api/staff/password": ChangePassword(call); return;
                case "GET /api/staff/intakes": ListIntakes(call); return;
                case "GET /api/staff/stats": Stats(call); return;
                case "GET /api/staff/export.xlsx": Export(call); return;
                case "POST /api/staff/demo-data": RequireAdmin(call); SeedDemo(call); return;
                case "POST /api/staff/ai/test": RequireAdmin(call); AiTest(call); return;
                case "GET /api/staff/access": Access(call); return;
                case "GET /api/staff/users": RequireAdmin(call); ListUsers(call); return;
                case "POST /api/staff/users": RequireAdmin(call); SaveUser(call); return;
                case "GET /api/staff/devices": RequireAdmin(call); ListDevices(call); return;
                case "POST /api/staff/devices/pair-code": RequireAdmin(call); CreatePairCode(call); return;
                case "GET /api/staff/settings": RequireAdmin(call); GetSettings(call); return;
                case "POST /api/staff/settings": RequireAdmin(call); SaveSettings(call); return;
                case "POST /api/staff/tunnel": RequireAdmin(call); SetTunnel(call); return;
                case "GET /api/staff/audit": RequireAdmin(call); Audit(call); return;
            }

            var deviceMatch = DevicePath.Match(call.Path);
            if (deviceMatch.Success && call.Method == "POST")
            {
                RequireAdmin(call);
                RevokeDevice(call, deviceMatch.Groups[1].Value);
                return;
            }

            var intakeMatch = IntakePath.Match(call.Path);
            if (intakeMatch.Success)
            {
                var id = intakeMatch.Groups[1].Value;
                var action = intakeMatch.Groups[2].Success ? intakeMatch.Groups[2].Value.TrimStart('/') : string.Empty;
                if (call.Method == "GET" && action.Length == 0) { GetIntake(call, id); return; }
                if (call.Method == "DELETE" && action.Length == 0) { RequireAdmin(call); DeleteIntake(call, id); return; }
                if (call.Method == "POST" && action == "review") { Review(call, id); return; }
                if (call.Method == "POST" && action == "reject") { Reject(call, id); return; }
                if (call.Method == "POST" && action == "reopen") { Reopen(call, id); return; }
                if (call.Method == "POST" && action == "ai") { AiForIntake(call, id); return; }
                if (call.Method == "POST" && action == "ai-feedback") { AiFeedback(call, id); return; }
            }
            throw new ApiException(404, "not_found", "Không tìm thấy API.");
        }

        // ---------- session & setup ----------

        private void Bootstrap(HttpCall call)
        {
            var setupRequired = !host.Config.HasUsers;
            object user = null;
            if (call.Session != null)
                user = SessionView(call.Session);
            call.WriteJson(200, new Dictionary<string, object>
            {
                { "setupRequired", setupRequired },
                { "isLocalMachine", call.IsLocalMachine },
                { "hospitalName", host.Config.Read(c => c.HospitalName) },
                { "departmentName", host.Config.Read(c => c.DepartmentName) },
                { "user", user },
                { "aiEnabled", host.Ai.Enabled },
                { "version", host.Version },
                { "serverTime", TextUtil.Now() }
            });
        }

        private void Setup(HttpCall call)
        {
            if (host.Config.HasUsers) throw new ApiException(409, "already_setup", "Máy chủ đã được thiết lập.");
            if (!call.IsLocalMachine)
                throw new ApiException(403, "local_only", "Chỉ thiết lập lần đầu được trên chính máy chủ (http://localhost).");
            var body = call.ReadJson(8 * 1024);
            var username = Json.Str(body, "username").ToLowerInvariant();
            var password = Json.Str(body, "password");
            var hospitalName = TextUtil.Clean(Json.Str(body, "hospitalName"), 120, false);
            ValidateUsername(username);
            var weak = Passwords.ValidateStrength(password);
            if (weak != null) throw new ApiException(400, "weak_password", weak);
            var user = NewUser(username, TextUtil.Clean(Json.Str(body, "displayName"), 60, false), Roles.Admin, password);
            host.Config.Update(c =>
            {
                if (c.Users.Any(u => !u.Disabled)) throw new ApiException(409, "already_setup", "Máy chủ đã được thiết lập.");
                c.Users.Add(user);
                if (hospitalName.Length > 0) c.HospitalName = hospitalName;
            });
            Logs.Audit(username, "setup-admin", "-", call.ClientIp);
            var session = host.Sessions.Create(user);
            call.SetCookie(SessionCookie, session.Token, null);
            call.WriteJson(200, new Dictionary<string, object> { { "user", SessionView(session) } });
        }

        private void Login(HttpCall call)
        {
            var body = call.ReadJson(8 * 1024);
            var username = Json.Str(body, "username").ToLowerInvariant();
            var password = Json.Str(body, "password");
            var ipKey = "login-ip:" + call.ClientIp;
            var userKey = "login-user:" + username;
            if (host.Limiter.IsBlocked(ipKey, 10, TimeSpan.FromMinutes(15)) || host.Limiter.IsBlocked(userKey, 8, TimeSpan.FromMinutes(15)))
                throw new ApiException(429, "locked", "Đăng nhập sai quá nhiều lần. Vui lòng thử lại sau 15 phút.");
            var user = host.Config.Read(c => c.Users.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase)));
            if (user == null || user.Disabled || !Passwords.Verify(password, user))
            {
                host.Limiter.Record(ipKey);
                host.Limiter.Record(userKey);
                Logs.Audit(username.Length == 0 ? "-" : username, "login-failed", "-", call.ClientIp);
                throw new ApiException(401, "invalid_login", "Sai tên đăng nhập hoặc mật khẩu.");
            }
            host.Limiter.Reset(userKey);
            var session = host.Sessions.Create(user);
            call.SetCookie(SessionCookie, session.Token, null);
            Logs.Audit(user.Username, "login", "-", call.ClientIp);
            call.WriteJson(200, new Dictionary<string, object> { { "user", SessionView(session) } });
        }

        private void Logout(HttpCall call)
        {
            if (call.Session != null)
            {
                host.Sessions.Remove(call.Session.Token);
                Logs.Audit(call.Session.Username, "logout", "-", call.ClientIp);
            }
            call.SetCookie(SessionCookie, string.Empty, TimeSpan.Zero);
            call.WriteJson(200, new Dictionary<string, object> { { "ok", true } });
        }

        private void ChangePassword(HttpCall call)
        {
            var body = call.ReadJson(8 * 1024);
            var current = Json.Str(body, "currentPassword");
            var next = Json.Str(body, "newPassword");
            var weak = Passwords.ValidateStrength(next);
            if (weak != null) throw new ApiException(400, "weak_password", weak);
            if (current == next) throw new ApiException(400, "same_password", "Mật khẩu mới phải khác mật khẩu cũ.");
            StaffUser updated = null;
            host.Config.Update(c =>
            {
                var user = c.Users.FirstOrDefault(u => string.Equals(u.Username, call.Session.Username, StringComparison.OrdinalIgnoreCase));
                if (user == null || !Passwords.Verify(current, user))
                    throw new ApiException(400, "invalid_password", "Mật khẩu hiện tại không đúng.");
                string salt, hash;
                Passwords.Hash(next, out salt, out hash, Passwords.DefaultIterations);
                user.Salt = salt;
                user.Hash = hash;
                user.Iterations = Passwords.DefaultIterations;
                user.MustChangePassword = false;
                updated = user;
            });
            host.Sessions.UpdateUser(updated);
            Logs.Audit(call.Session.Username, "change-password", "-", call.ClientIp);
            call.WriteJson(200, new Dictionary<string, object> { { "user", SessionView(call.Session) } });
        }

        // ---------- intakes ----------

        private void ListIntakes(HttpCall call)
        {
            var status = (call.Query["status"] ?? "pending").ToLowerInvariant();
            var q = (call.Query["q"] ?? string.Empty).Trim();
            var foldedQ = TextUtil.FoldName(q);
            var digitsQ = TextUtil.Digits(q);
            var idQ = TextUtil.FoldId(q);
            Func<IntakeRecord, bool> statusFilter;
            switch (status)
            {
                case "pending": statusFilter = r => r.Status == IntakeStatus.Pending; break;
                case "approved": statusFilter = r => r.Status == IntakeStatus.Approved || r.Status == IntakeStatus.Claimed; break;
                case "completed": statusFilter = r => r.Status == IntakeStatus.Completed; break;
                case "rejected": statusFilter = r => r.Status == IntakeStatus.Rejected; break;
                default: statusFilter = r => true; break;
            }
            Func<IntakeRecord, bool> filter = r => statusFilter(r) && (q.Length == 0 ||
                TextUtil.FoldId(r.Code) == idQ ||
                (foldedQ.Length >= 2 && TextUtil.FoldName(r.Patient.FullName).Contains(foldedQ)) ||
                (digitsQ.Length >= 4 && TextUtil.Digits(r.Patient.Phone).Contains(digitsQ)) ||
                (idQ.Length >= 3 && (TextUtil.FoldId(r.Review.HisPatientId) == idQ || TextUtil.FoldId(r.Patient.HisPatientId) == idQ)));

            var items = host.Store.Select(filter, r => new KeyValuePair<DateTimeOffset, Dictionary<string, object>>(
                TextUtil.ParseTime(status == "pending" ? r.CreatedAt : r.UpdatedAt), Views.StaffSummary(r)));
            var ordered = status == "pending"
                ? items.OrderBy(p => p.Key)
                : items.OrderByDescending(p => p.Key);
            call.WriteJson(200, new Dictionary<string, object>
            {
                { "items", ordered.Take(300).Select(p => p.Value).ToList() },
                { "counts", host.Store.Counts() },
                { "serverTime", TextUtil.Now() },
                { "changeStamp", host.ChangeStamp }
            });
        }

        private void GetIntake(HttpCall call, string id)
        {
            var record = host.Store.Get(id);
            if (record == null) throw new ApiException(404, "not_found", "Không tìm thấy tờ khai.");
            Logs.Audit(call.Session.Username, "view", record.Code, call.ClientIp);
            call.WriteJson(200, Views.StaffDetail(record));
        }

        private void Review(HttpCall call, string id)
        {
            var body = call.ReadJson(96 * 1024);
            var action = Json.Str(body, "action");
            if (action != "save" && action != "approve") throw new ApiException(400, "invalid_action", "Thao tác không hợp lệ.");
            var hisPatientId = IntakeValidator.NormalizeHisId(Json.Str(body, "hisPatientId"), true);
            var fields = IntakeValidator.SanitizeFields(Json.Obj(body, "fields"));
            var note = TextUtil.Clean(Json.Str(body, "note"), 1000, true);
            var requireHisId = host.Config.Read(c => c.RequireHisPatientIdOnApprove);
            if (action == "approve" && requireHisId && hisPatientId.Length == 0)
                throw new ApiException(400, "his_id_required", "Cần nhập mã bệnh nhân trên HIS trước khi duyệt.");
            if (action == "approve" && !fields.Any(f => f.Value.Length > 0))
                throw new ApiException(400, "empty_fields", "Chưa có nội dung nào để chuyển cho bác sĩ.");
            var actor = call.Session.Username;
            var updated = host.Store.Update(id, actor, action == "approve" ? "approved" : "edited", r =>
            {
                if (r.Status == IntakeStatus.Claimed || r.Status == IntakeStatus.Completed)
                    throw new ApiException(409, "locked", "Bác sĩ đang xử lý hoặc đã nhập HIS. Hãy mở lại (Mở lại tờ khai) nếu cần sửa.");
                r.Review.HisPatientId = hisPatientId;
                r.Review.Fields = fields;
                r.Review.Note = note;
                r.Review.ReviewedBy = actor;
                r.Review.ReviewedAt = TextUtil.Now();
                if (action == "approve")
                {
                    r.Status = IntakeStatus.Approved;
                    r.Review.ApprovedBy = call.Session.DisplayName;
                    r.Review.ApprovedAt = TextUtil.Now();
                    r.Review.RejectReason = string.Empty;
                }
            });
            Logs.Audit(actor, action == "approve" ? "approve" : "edit", updated.Code, call.ClientIp);
            host.NotifyChanged();
            call.WriteJson(200, Views.StaffDetail(updated));
        }

        private void Reject(HttpCall call, string id)
        {
            var body = call.ReadJson(8 * 1024);
            var reason = TextUtil.Clean(Json.Str(body, "reason"), 300, false);
            if (reason.Length == 0) throw new ApiException(400, "reason_required", "Vui lòng ghi lý do từ chối.");
            var actor = call.Session.Username;
            var updated = host.Store.Update(id, actor, "rejected", r =>
            {
                if (r.Status == IntakeStatus.Completed) throw new ApiException(409, "locked", "Tờ khai đã được nhập HIS.");
                r.Status = IntakeStatus.Rejected;
                r.Review.RejectedBy = call.Session.DisplayName;
                r.Review.RejectedAt = TextUtil.Now();
                r.Review.RejectReason = reason;
            });
            Logs.Audit(actor, "reject", updated.Code, call.ClientIp);
            host.NotifyChanged();
            call.WriteJson(200, Views.StaffDetail(updated));
        }

        private void Reopen(HttpCall call, string id)
        {
            var actor = call.Session.Username;
            var updated = host.Store.Update(id, actor, "reopened", r =>
            {
                r.Status = IntakeStatus.Pending;
                r.Review.ApprovedAt = string.Empty;
                r.Review.ApprovedBy = string.Empty;
                r.Agent.ClaimedAt = string.Empty;
                r.Agent.DeviceId = string.Empty;
            });
            Logs.Audit(actor, "reopen", updated.Code, call.ClientIp);
            host.NotifyChanged();
            call.WriteJson(200, Views.StaffDetail(updated));
        }

        private void DeleteIntake(HttpCall call, string id)
        {
            var record = host.Store.Get(id);
            if (record == null || !host.Store.Delete(id)) throw new ApiException(404, "not_found", "Không tìm thấy tờ khai.");
            Logs.Audit(call.Session.Username, "delete", record.Code, call.ClientIp);
            host.NotifyChanged();
            call.WriteJson(200, new Dictionary<string, object> { { "ok", true } });
        }

        // ---------- access / tunnel ----------

        private void Access(HttpCall call)
        {
            var publicPort = host.Config.Read(c => c.PublicPort);
            var staffPort = host.Config.Read(c => c.StaffPort);
            var addresses = host.LanAddresses();
            var tunnel = host.Tunnel.Snapshot();
            var staffTunnel = host.StaffTunnel.Snapshot();
            var configuredPublic = host.Config.Read(c => c.PublicBaseUrl);
            call.WriteJson(200, new Dictionary<string, object>
            {
                { "hospitalName", host.Config.Read(c => c.HospitalName) },
                { "demoMode", host.Config.Read(c => c.AllowPublicStaffAccess) },
                { "demoStaffUrl", staffTunnel.PublicUrl ?? string.Empty },
                { "demoStaffMessage", staffTunnel.Message ?? string.Empty },
                { "lanPatientUrls", addresses.Select(a => "http://" + a + ":" + publicPort + "/").ToList() },
                { "lanStaffUrls", addresses.Select(a => "http://" + a + ":" + staffPort + "/").ToList() },
                { "publicBaseUrl", configuredPublic },
                { "internetUrl", configuredPublic.Length > 0 ? configuredPublic : (tunnel.PublicUrl ?? string.Empty) },
                { "localOnly", host.LocalOnly },
                { "tunnel", TunnelView(tunnel) }
            });
        }

        private void SetTunnel(HttpCall call)
        {
            var body = call.ReadJson(4 * 1024);
            var mode = Json.Str(body, "mode") == "quick" ? "quick" : "off";
            host.Config.Update(c => c.TunnelMode = mode);
            host.ApplyTunnelMode();
            Logs.Audit(call.Session.Username, "tunnel-" + mode, "-", call.ClientIp);
            call.WriteJson(200, TunnelView(host.Tunnel.Snapshot()));
        }

        private static Dictionary<string, object> TunnelView(TunnelStatus status)
        {
            return new Dictionary<string, object>
            {
                { "mode", status.Mode },
                { "running", status.Running },
                { "publicUrl", status.PublicUrl ?? string.Empty },
                { "message", status.Message ?? string.Empty },
                { "cloudflaredFound", status.CloudflaredFound },
                { "startedAt", status.StartedAt ?? string.Empty }
            };
        }

        // ---------- users ----------

        private void ListUsers(HttpCall call)
        {
            var users = host.Config.Read(c => c.Users.Select(Views.User).ToList());
            call.WriteJson(200, new Dictionary<string, object> { { "items", users } });
        }

        private void SaveUser(HttpCall call)
        {
            var body = call.ReadJson(8 * 1024);
            var username = Json.Str(body, "username").ToLowerInvariant();
            ValidateUsername(username);
            var displayName = TextUtil.Clean(Json.Str(body, "displayName"), 60, false);
            var role = ParseRole(Json.Str(body, "role"));
            var password = Json.Str(body, "password");
            var disabled = Json.Bool(body, "disabled");
            if (password.Length > 0)
            {
                var weak = Passwords.ValidateStrength(password);
                if (weak != null) throw new ApiException(400, "weak_password", weak);
            }
            StaffUser saved = null;
            var created = false;
            host.Config.Update(c =>
            {
                var user = c.Users.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
                if (user == null)
                {
                    if (password.Length == 0) throw new ApiException(400, "password_required", "Cần đặt mật khẩu tạm cho tài khoản mới.");
                    user = NewUser(username, displayName, role, password);
                    user.MustChangePassword = !Json.Bool(body, "skipPasswordChange");
                    c.Users.Add(user);
                    created = true;
                }
                else
                {
                    if (string.Equals(user.Username, call.Session.Username, StringComparison.OrdinalIgnoreCase) && (disabled || role != Roles.Admin))
                        throw new ApiException(400, "self_lockout", "Không thể tự khóa hoặc tự hạ quyền tài khoản đang đăng nhập.");
                    user.DisplayName = displayName.Length > 0 ? displayName : user.DisplayName;
                    user.Role = role;
                    user.Disabled = disabled;
                    if (password.Length > 0)
                    {
                        string salt, hash;
                        Passwords.Hash(password, out salt, out hash, Passwords.DefaultIterations);
                        user.Salt = salt;
                        user.Hash = hash;
                        user.Iterations = Passwords.DefaultIterations;
                        user.MustChangePassword = true;
                    }
                }
                if (!c.Users.Any(u => u.Role == Roles.Admin && !u.Disabled))
                    throw new ApiException(400, "no_admin", "Phải còn ít nhất một quản trị viên.");
                saved = user;
            });
            if (saved.Disabled || password.Length > 0) host.Sessions.RemoveUser(saved.Username);
            else host.Sessions.UpdateUser(saved);
            Logs.Audit(call.Session.Username, created ? "user-create" : "user-update", saved.Username, call.ClientIp);
            call.WriteJson(200, Views.User(saved));
        }

        // ---------- devices (HIS assistants) ----------

        private void ListDevices(HttpCall call)
        {
            var devices = host.Config.Read(c => c.Devices.Select(Views.Device).ToList());
            call.WriteJson(200, new Dictionary<string, object> { { "items", devices } });
        }

        private void CreatePairCode(HttpCall call)
        {
            DateTime expires;
            var code = host.Pairing.Create(call.Session.Username, TimeSpan.FromMinutes(10), out expires);
            Logs.Audit(call.Session.Username, "pair-code", "-", call.ClientIp);
            var staffPort = host.Config.Read(c => c.StaffPort);
            call.WriteJson(200, new Dictionary<string, object>
            {
                { "code", code },
                { "expiresAt", new DateTimeOffset(expires).ToLocalTime().ToString("o", CultureInfo.InvariantCulture) },
                { "serverUrls", host.LanAddresses().Select(a => "http://" + a + ":" + staffPort + "/").ToList() }
            });
        }

        private void RevokeDevice(HttpCall call, string id)
        {
            string name = null;
            host.Config.Update(c =>
            {
                var device = c.Devices.FirstOrDefault(d => d.Id == id);
                if (device == null) throw new ApiException(404, "not_found", "Không tìm thấy máy.");
                device.Revoked = true;
                name = device.Name;
            });
            Logs.Audit(call.Session.Username, "device-revoke", name, call.ClientIp);
            call.WriteJson(200, new Dictionary<string, object> { { "ok", true } });
        }

        // ---------- settings / audit ----------

        private void GetSettings(HttpCall call)
        {
            call.WriteJson(200, host.Config.Read(c => new Dictionary<string, object>
            {
                { "hospitalName", c.HospitalName },
                { "departmentName", c.DepartmentName },
                { "publicBaseUrl", c.PublicBaseUrl },
                { "retentionDaysPending", c.RetentionDaysPending },
                { "retentionDaysDone", c.RetentionDaysDone },
                { "requireHisPatientIdOnApprove", c.RequireHisPatientIdOnApprove },
                { "allowPublicStaffAccess", c.AllowPublicStaffAccess },
                { "aiEnabled", c.AiEnabled },
                { "aiModel", c.AiModel },
                { "aiEndpoint", c.AiEndpoint },
                { "aiKeySet", c.AiApiKeyProtected.Length > 0 },
                { "maxPending", c.MaxPending },
                { "publicSubmitLimitPerIp", c.PublicSubmitLimitPerIp },
                { "staffPort", c.StaffPort },
                { "publicPort", c.PublicPort },
                { "tunnelMode", c.TunnelMode },
                { "dataDirectory", host.DataDirectory }
            }));
        }

        private void SaveSettings(HttpCall call)
        {
            var body = call.ReadJson(8 * 1024);
            var publicBaseUrl = Json.Str(body, "publicBaseUrl");
            if (publicBaseUrl.Length > 0 && !Regex.IsMatch(publicBaseUrl, @"^https?://[A-Za-z0-9.\-]+(:\d+)?(/[^\s]*)?$"))
                throw new ApiException(400, "invalid_url", "Địa chỉ công khai phải bắt đầu bằng https://");
            host.Config.Update(c =>
            {
                var name = TextUtil.Clean(Json.Str(body, "hospitalName"), 120, false);
                if (name.Length > 0) c.HospitalName = name;
                c.DepartmentName = TextUtil.Clean(Json.Str(body, "departmentName"), 120, false);
                c.PublicBaseUrl = publicBaseUrl.TrimEnd('/');
                c.RetentionDaysPending = Json.Int(body, "retentionDaysPending", c.RetentionDaysPending);
                c.RetentionDaysDone = Json.Int(body, "retentionDaysDone", c.RetentionDaysDone);
                c.RequireHisPatientIdOnApprove = Json.Bool(body, "requireHisPatientIdOnApprove");
                c.AllowPublicStaffAccess = Json.Bool(body, "allowPublicStaffAccess");
                c.AiEnabled = Json.Bool(body, "aiEnabled");
                var model = TextUtil.Clean(Json.Str(body, "aiModel"), 80, false);
                c.AiModel = model.Length > 0 ? model : AiAssistant.DefaultModel;
                var endpoint = TextUtil.Clean(Json.Str(body, "aiEndpoint"), 300, false);
                if (endpoint.Length > 0 && !Regex.IsMatch(endpoint, @"^https?://[A-Za-z0-9.\-]+(:\d+)?(/[^\s]*)?$"))
                    throw new ApiException(400, "invalid_url", "Địa chỉ dịch vụ AI không hợp lệ.");
                c.AiEndpoint = endpoint;
                var apiKey = Json.Str(body, "aiApiKey").Trim();
                if (apiKey == "-") c.AiApiKeyProtected = string.Empty;                       // explicit clear
                else if (apiKey.Length > 0) c.AiApiKeyProtected = Convert.ToBase64String(host.Protector.Protect(apiKey)); // new key
                c.MaxPending = Json.Int(body, "maxPending", c.MaxPending);
                c.PublicSubmitLimitPerIp = Json.Int(body, "publicSubmitLimitPerIp", c.PublicSubmitLimitPerIp);
            });
            host.ApplyTunnelMode();
            Logs.Audit(call.Session.Username, "settings", "-", call.ClientIp);
            GetSettings(call);
        }

        // ---------- dashboard / export / demo ----------

        private void Stats(HttpCall call)
        {
            call.WriteJson(200, Reports.Stats(host.Store, Days(call, 14)));
        }

        private void Export(HttpCall call)
        {
            var status = (call.Query["status"] ?? string.Empty).Trim().ToLowerInvariant();
            if (status.Length > 0 && !IntakeStatus.All.Contains(status)) status = string.Empty;
            var days = Days(call, 30);
            var bytes = Reports.ExportXlsx(host.Store, days, status);
            Logs.Audit(call.Session.Username, "export-xlsx", days + "d" + (status.Length > 0 ? "/" + status : string.Empty), call.ClientIp);
            var name = "to-khai-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".xlsx";
            call.WriteFile(200, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name, bytes);
        }

        private void SeedDemo(HttpCall call)
        {
            var created = DemoData.Seed(host.Store, call.Session.Username);
            host.NotifyChanged();
            call.WriteJson(200, new Dictionary<string, object> { { "created", created } });
        }

        // ---------- controlled AI ----------

        private void AiForIntake(HttpCall call, string id)
        {
            var record = host.Store.Get(id);
            if (record == null) throw new ApiException(404, "not_found", "Không tìm thấy tờ khai.");
            if (!host.Limiter.Allow("ai:" + call.Session.Username, 40, TimeSpan.FromMinutes(10)))
                throw new ApiException(429, "rate_limited", "Bạn đã dùng trợ lý AI quá nhiều lần trong 10 phút. Vui lòng chờ.");
            var body = call.ReadJson(8 * 1024);
            var task = Json.Str(body, "task");
            if (task == "draft")
            {
                var result = host.Ai.Draft(record);
                Logs.Audit(call.Session.Username, "ai-draft", record.Code, call.ClientIp);
                call.WriteJson(200, result);
                return;
            }
            if (task == "ask")
            {
                var question = TextUtil.Clean(Json.Str(body, "question"), 500, false);
                if (question.Length < 3) throw new ApiException(400, "question_required", "Hãy nhập câu hỏi về tờ khai này.");
                var result = host.Ai.Ask(record, question);
                Logs.Audit(call.Session.Username, Json.Bool(result, "inScope") ? "ai-ask" : "ai-ask-out-of-scope", record.Code, call.ClientIp);
                call.WriteJson(200, result);
                return;
            }
            throw new ApiException(400, "invalid_task", "Tác vụ AI không hợp lệ.");
        }

        /// <summary>The nurse's verdict on an AI draft (accepted N fields / rejected) — audit only, no content.</summary>
        private void AiFeedback(HttpCall call, string id)
        {
            var record = host.Store.Get(id);
            if (record == null) throw new ApiException(404, "not_found", "Không tìm thấy tờ khai.");
            var body = call.ReadJson(4 * 1024);
            var decision = Json.Str(body, "decision");
            if (decision != "accept" && decision != "partial" && decision != "reject" && decision != "edited")
                throw new ApiException(400, "invalid_decision", "Phản hồi không hợp lệ.");
            var fields = Math.Max(0, Math.Min(50, Json.Int(body, "fields", 0)));
            Logs.Audit(call.Session.Username, "ai-" + decision, record.Code + " (" + fields + " trường)", call.ClientIp);
            call.WriteJson(200, new Dictionary<string, object> { { "ok", true } });
        }

        private void AiTest(HttpCall call)
        {
            var reply = host.Ai.Test();
            Logs.Audit(call.Session.Username, "ai-test", "-", call.ClientIp);
            call.WriteJson(200, new Dictionary<string, object> { { "ok", true }, { "reply", reply }, { "model", host.Config.Read(c => c.AiModel) } });
        }

        private static int Days(HttpCall call, int fallback)
        {
            int days;
            if (!int.TryParse(call.Query["days"], out days)) days = fallback;
            return Math.Max(1, Math.Min(365, days));
        }

        private void Audit(HttpCall call)
        {
            var limit = 300;
            int parsed;
            if (int.TryParse(call.Query["limit"], out parsed)) limit = Math.Max(10, Math.Min(2000, parsed));
            var file = Path.Combine(host.LogDirectory, "audit-" + DateTime.Now.ToString("yyyyMM") + ".log");
            var lines = new List<string>();
            if (File.Exists(file))
            {
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null) lines.Add(line);
                }
            }
            call.WriteJson(200, new Dictionary<string, object>
            {
                { "items", lines.Skip(Math.Max(0, lines.Count - limit)).Reverse().ToList() }
            });
        }

        // ---------- helpers ----------

        private static StaffUser NewUser(string username, string displayName, string role, string password)
        {
            string salt, hash;
            Passwords.Hash(password, out salt, out hash, Passwords.DefaultIterations);
            return new StaffUser
            {
                Username = username,
                DisplayName = displayName.Length > 0 ? displayName : username,
                Role = role,
                Salt = salt,
                Hash = hash,
                Iterations = Passwords.DefaultIterations,
                CreatedAt = TextUtil.Now()
            };
        }

        private static void ValidateUsername(string username)
        {
            if (!UsernamePattern.IsMatch(username ?? string.Empty))
                throw new ApiException(400, "invalid_username", "Tên đăng nhập 3–32 ký tự: chữ thường không dấu, số, dấu chấm, gạch dưới hoặc gạch ngang.");
        }

        private static Dictionary<string, object> SessionView(StaffSession session)
        {
            return new Dictionary<string, object>
            {
                { "username", session.Username },
                { "displayName", session.DisplayName },
                { "role", session.Role },
                { "mustChangePassword", session.MustChangePassword }
            };
        }

        private static void RequireSession(HttpCall call)
        {
            if (call.Session == null) throw new ApiException(401, "login_required", "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.");
        }

        private static void RequireAdmin(HttpCall call)
        {
            if (!call.Session.IsAdmin) throw new ApiException(403, "admin_only", "Chức năng dành cho quản trị viên.");
        }

        /// <summary>Admin/Nurse/Doctor only — anything else (including empty/unknown) falls back to Nurse, the least-privileged role.</summary>
        private static string ParseRole(string value)
        {
            if (value == Roles.Admin) return Roles.Admin;
            if (value == Roles.Doctor) return Roles.Doctor;
            return Roles.Nurse;
        }
    }
}
