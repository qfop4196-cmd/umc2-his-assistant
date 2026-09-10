using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Umc2.IntakeServer
{
    /// <summary>
    /// API for the Windows HIS assistant on doctors' PCs. Devices authenticate with a per-device bearer token
    /// obtained once through a 6-digit pairing code created by an admin. Tokens are stored hashed and can be revoked.
    /// </summary>
    internal sealed class AgentEndpoints
    {
        private static readonly Regex IntakePath = new Regex("^/api/agent/intakes/([a-f0-9]{24})(/[a-z]+)?$", RegexOptions.Compiled);
        private readonly ServerHost host;

        public AgentEndpoints(ServerHost host)
        {
            this.host = host;
        }

        public void Handle(HttpCall call)
        {
            if (call.Method == "POST" && call.Path == "/api/agent/pair")
            {
                Pair(call);
                return;
            }

            Authenticate(call);
            switch (call.Method + " " + call.Path)
            {
                case "GET /api/agent/ping": Ping(call); return;
                case "POST /api/agent/intakes/match": Match(call); return;
                case "GET /api/agent/intakes": Recent(call); return;
                case "POST /api/agent/jobs/claim": LegacyClaim(call); return;
                case "POST /api/agent/jobs/complete": LegacyComplete(call); return;
            }
            var match = IntakePath.Match(call.Path);
            if (match.Success)
            {
                var id = match.Groups[1].Value;
                var action = match.Groups[2].Success ? match.Groups[2].Value.TrimStart('/') : string.Empty;
                if (call.Method == "GET" && action.Length == 0) { Detail(call, id); return; }
                if (call.Method == "POST" && action == "claim") { Claim(call, id); return; }
                if (call.Method == "POST" && action == "release") { Release(call, id); return; }
                if (call.Method == "POST" && action == "complete") { Complete(call, id); return; }
            }
            throw new ApiException(404, "not_found", "Không tìm thấy API.");
        }

        private void Pair(HttpCall call)
        {
            if (!host.Limiter.Allow("pair:" + call.ClientIp, 10, TimeSpan.FromMinutes(15)))
                throw new ApiException(429, "rate_limited", "Thử ghép nối quá nhiều lần. Vui lòng đợi 15 phút.");
            var body = call.ReadJson(4 * 1024);
            string createdBy;
            if (!host.Pairing.TryConsume(Json.Str(body, "code"), out createdBy))
            {
                Logs.Audit("-", "pair-failed", "-", call.ClientIp);
                throw new ApiException(400, "invalid_code", "Mã ghép nối không đúng hoặc đã hết hạn (10 phút).");
            }
            var name = TextUtil.Clean(Json.Str(body, "deviceName"), 60, false);
            var machine = TextUtil.Clean(Json.Str(body, "machineName"), 60, false);
            if (name.Length == 0) name = machine.Length > 0 ? machine : "May-bac-si";
            var token = Tokens.NewToken(32);
            var device = new AgentDevice
            {
                Id = Tokens.NewId(),
                Name = name,
                MachineName = machine,
                TokenHash = Tokens.Sha256Hex(token),
                CreatedAt = TextUtil.Now(),
                CreatedBy = createdBy,
                LastSeenAt = TextUtil.Now(),
                LastIp = call.ClientIp
            };
            host.Config.Update(c => c.Devices.Add(device));
            Logs.Audit(createdBy, "device-paired", device.Name, call.ClientIp);
            call.WriteJson(200, new Dictionary<string, object>
            {
                { "deviceId", device.Id },
                { "deviceName", device.Name },
                { "token", token },
                { "hospitalName", host.Config.Read(c => c.HospitalName) }
            });
        }

        private void Authenticate(HttpCall call)
        {
            var token = call.BearerToken();
            if (token.Length < 20) throw new ApiException(401, "unauthorized", "Máy này chưa được ghép nối với máy chủ tờ khai.");
            var hash = Tokens.Sha256Hex(token);
            var device = host.Config.Read(c =>
            {
                var found = c.Devices.FirstOrDefault(d => !d.Revoked && Tokens.FixedTimeEquals(d.TokenHash, hash));
                return found == null ? null : new AgentDevice { Id = found.Id, Name = found.Name, LastSeenAt = found.LastSeenAt, LastIp = found.LastIp };
            });
            if (device == null)
            {
                if (host.Limiter.Allow("agent-bad:" + call.ClientIp, 1, TimeSpan.FromMinutes(5)))
                    Logs.Audit("-", "agent-unauthorized", "-", call.ClientIp);
                throw new ApiException(401, "unauthorized", "Khóa của máy này không hợp lệ hoặc đã bị thu hồi. Hãy ghép nối lại.");
            }
            call.Device = device;
            // Persist "last seen" at most every 5 minutes to avoid rewriting the config on every poll.
            var lastSeen = TextUtil.ParseTime(device.LastSeenAt);
            if (DateTimeOffset.Now - lastSeen > TimeSpan.FromMinutes(5) || device.LastIp != call.ClientIp)
            {
                host.Config.Update(c =>
                {
                    var stored = c.Devices.FirstOrDefault(d => d.Id == device.Id);
                    if (stored == null) return;
                    stored.LastSeenAt = TextUtil.Now();
                    stored.LastIp = call.ClientIp;
                });
            }
        }

        private void Ping(HttpCall call)
        {
            call.WriteJson(200, new Dictionary<string, object>
            {
                { "ok", true },
                { "deviceName", call.Device.Name },
                { "hospitalName", host.Config.Read(c => c.HospitalName) },
                { "serverTime", TextUtil.Now() },
                { "version", host.Version },
                { "counts", host.Store.Counts() }
            });
        }

        private void Match(HttpCall call)
        {
            var body = call.ReadJson(4 * 1024);
            var patientId = Json.Str(body, "patientId");
            var patientName = Json.Str(body, "patientName");
            var birthYear = ParseYear(Json.Str(body, "birthYear"));
            var matches = host.Store.Match(patientId, patientName, birthYear);
            var result = matches.Take(10).Select(m =>
            {
                var view = Views.AgentDetail(m.Record);
                view["score"] = m.Score;
                view["matchReason"] = m.Reason;
                return view;
            }).ToList();
            call.WriteJson(200, new Dictionary<string, object>
            {
                { "matches", result },
                { "approvedCount", host.Store.CountByStatus(IntakeStatus.Approved) },
                { "changeStamp", host.ChangeStamp }
            });
        }

        private void Recent(HttpCall call)
        {
            var since = DateTimeOffset.Now.AddHours(-48);
            var items = host.Store.Select(
                r => (r.Status == IntakeStatus.Approved || r.Status == IntakeStatus.Claimed) && TextUtil.ParseTime(r.UpdatedAt) > since,
                r => Views.AgentSummary(r));
            call.WriteJson(200, new Dictionary<string, object>
            {
                { "items", items.OrderByDescending(i => Convert.ToString(i["approvedAt"])).Take(200).ToList() },
                { "changeStamp", host.ChangeStamp }
            });
        }

        private void Detail(HttpCall call, string id)
        {
            var record = host.Store.Get(id);
            if (record == null) throw new ApiException(404, "not_found", "Không tìm thấy tờ khai.");
            if (record.Status != IntakeStatus.Approved && record.Status != IntakeStatus.Claimed)
                throw new ApiException(409, "not_available", "Tờ khai chưa được duyệt hoặc đã được nhập HIS.");
            Logs.Audit(call.Device.Name, "agent-view", record.Code, call.ClientIp);
            call.WriteJson(200, Views.AgentDetail(record));
        }

        private void Claim(HttpCall call, string id)
        {
            var body = call.ReadJson(4 * 1024);
            var observed = IntakeValidator.NormalizeHisId(Json.Str(body, "hisPatientId"), false);
            var force = Json.Bool(body, "force");
            var device = call.Device;
            var updated = host.Store.Update(id, "device:" + device.Name, "claimed", r =>
            {
                if (r.Status != IntakeStatus.Approved && r.Status != IntakeStatus.Claimed)
                    throw new ApiException(409, "not_available", "Tờ khai chưa được duyệt hoặc đã được nhập HIS.");
                if (r.Status == IntakeStatus.Claimed && r.Agent.DeviceId != device.Id && !force)
                    throw new ApiException(409, "claimed_elsewhere", "Tờ khai đang được xử lý ở máy " + r.Agent.DeviceName + ".");
                var reviewed = TextUtil.FoldId(r.Review.HisPatientId);
                if (reviewed.Length > 0 && observed.Length > 0 && reviewed != TextUtil.FoldId(observed))
                    throw new ApiException(409, "patient_mismatch", "Mã BN đang mở trên HIS khác mã BN điều dưỡng đã xác nhận. Đã dừng.");
                r.Status = IntakeStatus.Claimed;
                r.Agent.DeviceId = device.Id;
                r.Agent.DeviceName = device.Name;
                r.Agent.ClaimedAt = TextUtil.Now();
                r.Agent.ObservedHisPatientId = observed;
            });
            Logs.Audit(device.Name, "agent-claim", updated.Code, call.ClientIp);
            host.NotifyChanged();
            call.WriteJson(200, Views.AgentDetail(updated));
        }

        private void Release(HttpCall call, string id)
        {
            var device = call.Device;
            var updated = host.Store.Update(id, "device:" + device.Name, "released", r =>
            {
                if (r.Status != IntakeStatus.Claimed) return;
                if (r.Agent.DeviceId != device.Id) throw new ApiException(409, "claimed_elsewhere", "Tờ khai đang được xử lý ở máy khác.");
                r.Status = IntakeStatus.Approved;
                r.Agent.ClaimedAt = string.Empty;
            });
            Logs.Audit(device.Name, "agent-release", updated.Code, call.ClientIp);
            host.NotifyChanged();
            call.WriteJson(200, Views.AgentSummary(updated));
        }

        private void Complete(HttpCall call, string id)
        {
            var body = call.ReadJson(8 * 1024);
            var result = Json.Str(body, "result");
            if (result != "completed" && result != "needs_human" && result != "failed") result = "completed";
            var summary = TextUtil.Clean(Json.Str(body, "summary"), 300, false);
            var observed = IntakeValidator.NormalizeHisId(Json.Str(body, "hisPatientId"), false);
            var filled = Math.Max(0, Json.Int(body, "filledCount", 0));
            var device = call.Device;
            var updated = host.Store.Update(id, "device:" + device.Name, result == "completed" ? "completed" : "agent-" + result, r =>
            {
                if (r.Status == IntakeStatus.Completed) return;
                if (r.Status != IntakeStatus.Approved && r.Status != IntakeStatus.Claimed)
                    throw new ApiException(409, "not_available", "Tờ khai không còn ở trạng thái chờ nhập HIS.");
                var reviewed = TextUtil.FoldId(r.Review.HisPatientId);
                if (reviewed.Length > 0 && observed.Length > 0 && reviewed != TextUtil.FoldId(observed))
                    throw new ApiException(409, "patient_mismatch", "Mã BN không khớp với mã điều dưỡng đã xác nhận.");
                r.Agent.DeviceId = device.Id;
                r.Agent.DeviceName = device.Name;
                r.Agent.Result = result;
                r.Agent.Summary = summary;
                r.Agent.FilledCount = filled;
                if (observed.Length > 0) r.Agent.ObservedHisPatientId = observed;
                if (result == "completed")
                {
                    r.Status = IntakeStatus.Completed;
                    r.Agent.CompletedAt = TextUtil.Now();
                    if (r.Review.HisPatientId.Length == 0 && observed.Length > 0) r.Review.HisPatientId = observed;
                }
                else
                {
                    r.Status = IntakeStatus.Approved;
                    r.Agent.ClaimedAt = string.Empty;
                }
            });
            Logs.Audit(device.Name, "agent-" + result, updated.Code, call.ClientIp);
            host.NotifyChanged();
            call.WriteJson(200, Views.AgentSummary(updated));
        }

        // ---------- compatibility with CloudQueueClient (/api/agent/jobs/*) ----------

        private void LegacyClaim(HttpCall call)
        {
            call.ReadJson(4 * 1024);
            var oldest = host.Store.Select(r => r.Status == IntakeStatus.Approved, r => new KeyValuePair<DateTimeOffset, string>(TextUtil.ParseTime(r.Review.ApprovedAt), r.Id))
                .OrderBy(p => p.Key).Select(p => p.Value).FirstOrDefault();
            if (oldest == null)
            {
                call.WriteJson(200, new Dictionary<string, object> { { "job", null } });
                return;
            }
            var device = call.Device;
            IntakeRecord updated;
            try
            {
                updated = host.Store.Update(oldest, "device:" + device.Name, "claimed", r =>
                {
                    if (r.Status != IntakeStatus.Approved) throw new ApiException(409, "race", "busy");
                    r.Status = IntakeStatus.Claimed;
                    r.Agent.DeviceId = device.Id;
                    r.Agent.DeviceName = device.Name;
                    r.Agent.ClaimedAt = TextUtil.Now();
                });
            }
            catch (ApiException ex)
            {
                if (ex.Code != "race") throw;
                call.WriteJson(200, new Dictionary<string, object> { { "job", null } });
                return;
            }
            host.NotifyChanged();
            call.WriteJson(200, new Dictionary<string, object>
            {
                {
                    "job", new Dictionary<string, object>
                    {
                        { "id", updated.Id },
                        { "admissionId", updated.Code },
                        {
                            "payload", new Dictionary<string, object>
                            {
                                {
                                    "form", new Dictionary<string, object>
                                    {
                                        { "code", "admission_sheet" },
                                        { "name", "Tờ khai trước khám " + updated.Code },
                                        { "signaturePolicy", "manual" }
                                    }
                                },
                                { "fields", Views.AgentFields(updated) }
                            }
                        }
                    }
                }
            });
        }

        private void LegacyComplete(HttpCall call)
        {
            var body = call.ReadJson(8 * 1024);
            var jobId = Json.Str(body, "jobId");
            if (!Regex.IsMatch(jobId, "^[a-f0-9]{24}$")) throw new ApiException(400, "invalid_job", "Mã tác vụ không hợp lệ.");
            var status = Json.Str(body, "status");
            var summary = TextUtil.Clean(Json.Str(Json.Obj(body, "result"), "summary"), 300, false);
            var device = call.Device;
            var completed = status == "completed";
            var updated = host.Store.Update(jobId, "device:" + device.Name, completed ? "completed" : "agent-needs_human", r =>
            {
                if (r.Status == IntakeStatus.Completed) return;
                r.Agent.Result = completed ? "completed" : "needs_human";
                r.Agent.Summary = summary;
                r.Agent.DeviceId = device.Id;
                r.Agent.DeviceName = device.Name;
                if (completed)
                {
                    r.Status = IntakeStatus.Completed;
                    r.Agent.CompletedAt = TextUtil.Now();
                }
                else
                {
                    r.Status = IntakeStatus.Approved;
                }
            });
            Logs.Audit(device.Name, "agent-" + (completed ? "completed" : "needs_human"), updated.Code, call.ClientIp);
            host.NotifyChanged();
            call.WriteJson(200, new Dictionary<string, object> { { "ok", true } });
        }

        private static int ParseYear(string value)
        {
            var digits = TextUtil.Digits(value);
            if (digits.Length >= 4)
            {
                int year;
                // Accept "1970", "12/05/1970" or "19700512": take the 4-digit group that looks like a year.
                var match = Regex.Match(value ?? string.Empty, @"(19|20)\d{2}");
                if (match.Success && int.TryParse(match.Value, out year)) return year;
            }
            return 0;
        }
    }
}
