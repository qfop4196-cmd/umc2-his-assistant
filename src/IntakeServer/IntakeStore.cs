using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Umc2.IntakeServer
{
    internal sealed class MatchResult
    {
        public IntakeRecord Record { get; set; }
        public int Score { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>
    /// Encrypted, file-per-record queue of patient intakes. This is a short-lived work queue, not the medical record:
    /// records are purged automatically after the retention period (the authoritative record lives in HIS).
    /// </summary>
    internal sealed class IntakeStore
    {
        private readonly string directory;
        private readonly string quarantineDirectory;
        private readonly DataProtector protector;
        private readonly object sync = new object();
        private readonly Dictionary<string, IntakeRecord> records = new Dictionary<string, IntakeRecord>(StringComparer.Ordinal);

        public IntakeStore(string directory, DataProtector protector)
        {
            this.directory = directory;
            this.protector = protector;
            quarantineDirectory = Path.Combine(directory, "_unreadable");
        }

        public int Load()
        {
            Directory.CreateDirectory(directory);
            lock (sync)
            {
                records.Clear();
                foreach (var path in Directory.GetFiles(directory, "*.dat"))
                {
                    try
                    {
                        var record = Json.Deserialize<IntakeRecord>(protector.Unprotect(File.ReadAllBytes(path)));
                        if (record == null || string.IsNullOrEmpty(record.Id)) throw new InvalidDataException("empty");
                        Normalize(record);
                        records[record.Id] = record;
                    }
                    catch (Exception ex)
                    {
                        Logs.Error("Không đọc được bản ghi " + Path.GetFileName(path) + " (đã chuyển vào _unreadable)", ex);
                        try
                        {
                            Directory.CreateDirectory(quarantineDirectory);
                            File.Move(path, Path.Combine(quarantineDirectory, Path.GetFileName(path) + "." + DateTime.Now.Ticks));
                        }
                        catch (IOException) { }
                    }
                }
                foreach (var tmp in Directory.GetFiles(directory, "*.tmp"))
                {
                    try { File.Delete(tmp); }
                    catch (IOException) { }
                }
                return records.Count;
            }
        }

        public IntakeRecord Add(IntakeRecord record, string actor)
        {
            lock (sync)
            {
                record.Id = Tokens.NewId();
                string code;
                do { code = Tokens.NewShortCode(); } while (records.Values.Any(r => r.Code == code));
                record.Code = code;
                record.Status = IntakeStatus.Pending;
                record.CreatedAt = record.UpdatedAt = TextUtil.Now();
                Normalize(record);
                AddHistory(record, actor, "submitted", record.Source);
                Save(record);
                records[record.Id] = record;
                return Clone(record);
            }
        }

        public IntakeRecord Get(string id)
        {
            lock (sync)
            {
                IntakeRecord record;
                return id != null && records.TryGetValue(id, out record) ? Clone(record) : null;
            }
        }

        public List<T> Select<T>(Func<IntakeRecord, bool> filter, Func<IntakeRecord, T> projector)
        {
            lock (sync) return records.Values.Where(filter).Select(projector).ToList();
        }

        public Dictionary<string, int> Counts()
        {
            lock (sync)
            {
                var result = IntakeStatus.All.ToDictionary(s => s, s => 0);
                foreach (var record in records.Values)
                {
                    int count;
                    result.TryGetValue(record.Status, out count);
                    result[record.Status] = count + 1;
                }
                return result;
            }
        }

        public int CountByStatus(string status)
        {
            lock (sync) return records.Values.Count(r => r.Status == status);
        }

        /// <summary>
        /// Applies <paramref name="mutate"/> under the store lock and persists the result.
        /// The delegate may throw <see cref="ApiException"/> to reject invalid transitions.
        /// </summary>
        public IntakeRecord Update(string id, string actor, string action, Action<IntakeRecord> mutate)
        {
            lock (sync)
            {
                IntakeRecord record;
                if (id == null || !records.TryGetValue(id, out record))
                    throw new ApiException(404, "not_found", "Không tìm thấy tờ khai (có thể đã hết hạn lưu trữ).");
                var working = Clone(record);
                mutate(working);
                working.UpdatedAt = TextUtil.Now();
                AddHistory(working, actor, action, null);
                Save(working);
                records[id] = working;
                return Clone(working);
            }
        }

        public bool Delete(string id)
        {
            lock (sync)
            {
                if (id == null || !records.ContainsKey(id)) return false;
                records.Remove(id);
                DeleteFile(id);
                return true;
            }
        }

        /// <summary>Deletes pending/rejected records older than <paramref name="pendingDays"/> and finished ones older than <paramref name="doneDays"/>.</summary>
        public List<string> Purge(int pendingDays, int doneDays)
        {
            var purged = new List<string>();
            var now = DateTimeOffset.Now;
            lock (sync)
            {
                foreach (var record in records.Values.ToList())
                {
                    var updated = TextUtil.ParseTime(record.UpdatedAt);
                    var created = TextUtil.ParseTime(record.CreatedAt);
                    bool expired;
                    switch (record.Status)
                    {
                        case IntakeStatus.Pending:
                            expired = now - created > TimeSpan.FromDays(pendingDays);
                            break;
                        case IntakeStatus.Approved:
                        case IntakeStatus.Claimed:
                            // Approved but never used by a doctor: keep a little longer than unreviewed ones.
                            expired = now - updated > TimeSpan.FromDays(Math.Max(pendingDays, doneDays));
                            break;
                        default:
                            expired = now - updated > TimeSpan.FromDays(doneDays);
                            break;
                    }
                    if (!expired) continue;
                    records.Remove(record.Id);
                    DeleteFile(record.Id);
                    purged.Add(record.Code);
                }
            }
            return purged;
        }

        /// <summary>Returns claimed records whose doctor PC went silent to the approved queue.</summary>
        public List<string> ReleaseStaleClaims(TimeSpan maxAge)
        {
            var released = new List<string>();
            var now = DateTimeOffset.Now;
            lock (sync)
            {
                foreach (var record in records.Values.Where(r => r.Status == IntakeStatus.Claimed).ToList())
                {
                    if (now - TextUtil.ParseTime(record.Agent.ClaimedAt) <= maxAge) continue;
                    var working = Clone(record);
                    working.Status = IntakeStatus.Approved;
                    working.Agent.ClaimedAt = string.Empty;
                    working.UpdatedAt = TextUtil.Now();
                    AddHistory(working, "system", "claim-expired", working.Agent.DeviceName);
                    Save(working);
                    records[working.Id] = working;
                    released.Add(working.Code);
                }
            }
            return released;
        }

        /// <summary>
        /// Finds approved intakes for the patient currently open in HIS.
        /// A reviewed HIS patient ID that differs from the one on screen excludes the record entirely.
        /// </summary>
        public List<MatchResult> Match(string hisPatientId, string patientName, int birthYear)
        {
            var id = TextUtil.FoldId(hisPatientId);
            var name = TextUtil.FoldName(patientName);
            var results = new List<MatchResult>();
            lock (sync)
            {
                foreach (var record in records.Values)
                {
                    if (record.Status != IntakeStatus.Approved && record.Status != IntakeStatus.Claimed) continue;
                    var reviewedId = TextUtil.FoldId(record.Review.HisPatientId);
                    var selfId = TextUtil.FoldId(record.Patient.HisPatientId);
                    var recordName = TextUtil.FoldName(record.Patient.FullName);
                    var nameMatches = name.Length > 0 && recordName == name;
                    var yearMatches = birthYear > 0 && record.Patient.BirthYear == birthYear;
                    var yearConflicts = birthYear > 0 && record.Patient.BirthYear > 0 && record.Patient.BirthYear != birthYear;

                    var score = 0;
                    string reason = null;
                    if (id.Length > 0 && reviewedId.Length > 0)
                    {
                        if (reviewedId != id) continue;
                        score = 100;
                        reason = "Trùng mã BN do điều dưỡng xác nhận";
                    }
                    else if (id.Length > 0 && selfId.Length > 0 && selfId == id)
                    {
                        score = nameMatches && !yearConflicts ? 95 : 80;
                        reason = nameMatches ? "Trùng mã BN (BN tự khai) + họ tên" : "Trùng mã BN do người bệnh tự khai";
                    }
                    else if (nameMatches && !yearConflicts)
                    {
                        score = yearMatches ? 90 : 60;
                        reason = yearMatches ? "Trùng họ tên + năm sinh" : "Chỉ trùng họ tên";
                    }
                    if (score == 0) continue;
                    results.Add(new MatchResult { Record = Clone(record), Score = score, Reason = reason });
                }
            }
            return results
                .OrderByDescending(r => r.Score)
                .ThenByDescending(r => TextUtil.ParseTime(r.Record.Review.ApprovedAt))
                .ToList();
        }

        private static void Normalize(IntakeRecord record)
        {
            if (record.Patient == null) record.Patient = new PatientInfo();
            if (record.Answers == null) record.Answers = new Dictionary<string, object>();
            if (record.Review == null) record.Review = new ReviewInfo();
            if (record.Review.Fields == null) record.Review.Fields = new Dictionary<string, string>();
            if (record.Agent == null) record.Agent = new AgentInfo();
            if (record.History == null) record.History = new List<HistoryEntry>();
        }

        private static void AddHistory(IntakeRecord record, string actor, string action, string detail)
        {
            record.History.Add(new HistoryEntry { At = TextUtil.Now(), Actor = actor ?? "-", Action = action, Detail = detail ?? string.Empty });
            if (record.History.Count > 60) record.History.RemoveRange(0, record.History.Count - 60);
        }

        private static IntakeRecord Clone(IntakeRecord record)
        {
            var copy = Json.Deserialize<IntakeRecord>(Json.Serialize(record));
            Normalize(copy);
            return copy;
        }

        private void Save(IntakeRecord record)
        {
            var path = Path.Combine(directory, record.Id + ".dat");
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, protector.Protect(Json.Serialize(record)));
            FileUtil.ReplaceFile(tmp, path);
        }

        private void DeleteFile(string id)
        {
            var path = Path.Combine(directory, id + ".dat");
            try
            {
                if (!File.Exists(path)) return;
                // Overwrite before delete so the ciphertext is not trivially recoverable from free space.
                var length = new FileInfo(path).Length;
                File.WriteAllBytes(path, new byte[Math.Min(length, 1024 * 1024)]);
                File.Delete(path);
            }
            catch (IOException ex)
            {
                Logs.Error("Không xóa được tệp bản ghi", ex);
            }
        }
    }
}
