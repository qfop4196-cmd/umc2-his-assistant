using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Umc2.IntakeServer
{
    /// <summary>
    /// Sample records for demonstrations and judging: obviously fictional patients ("Mẫu", "Thử", "Demo"),
    /// phone numbers in the reserved 0900 000 0xx range, HIS codes BN-TEST-0xx. Never used in production data.
    /// </summary>
    internal static class DemoData
    {
        private static readonly string[][] Patients =
        {
            // name, birth year, gender, chief complaint, onset (value unit), extra answers as "key=value;..."
            new[] { "Nguyễn Văn Mẫu", "1968", "male", "Đau ngực trái khi gắng sức", "3 ngày", "painScore=6;chronicConditions=Tăng huyết áp, Đái tháo đường;medications=Amlodipine 5mg, Metformin 500mg;smoking=Đang hút;allergyStatus=Không" },
            new[] { "Trần Thị Thử", "1985", "female", "Đau bụng vùng hạ sườn phải", "2 ngày", "painScore=7;associatedSymptoms=Buồn nôn, Sốt nhẹ;chronicConditions=Không;allergyStatus=Có;allergyDrugs=Penicillin;allergyReaction=Nổi mề đay" },
            new[] { "Lê Minh Demo", "1992", "male", "Ho kéo dài, khạc đàm vàng", "2 tuần", "painScore=2;associatedSymptoms=Sốt về chiều, Sụt cân;smoking=Đã bỏ;allergyStatus=Không" },
            new[] { "Phạm Thị Kiểm Thử", "1957", "female", "Chóng mặt, choáng váng khi đứng dậy", "1 tuần", "painScore=0;chronicConditions=Tăng huyết áp;medications=Losartan 50mg;allergyStatus=Không;familyConditions=Đột quỵ" },
            new[] { "Hoàng Văn Ví Dụ", "1979", "male", "Đau lưng lan xuống chân phải", "10 ngày", "painScore=8;priorTreatment=Có;priorTreatmentDetail=Uống thuốc giảm đau tại nhà thuốc;chronicConditions=Không;allergyStatus=Không" },
            new[] { "Võ Thị Mẫu Thử", "2001", "female", "Sốt cao, đau đầu, đau mỏi người", "3 ngày", "painScore=5;associatedSymptoms=Chảy máu chân răng;pregnancy=Không;allergyStatus=Không" },
            new[] { "Đặng Văn Demo", "1948", "male", "Khó thở khi nằm, phù hai chân", "5 ngày", "painScore=3;chronicConditions=Suy tim, Tăng huyết áp;medications=Furosemide 40mg, Bisoprolol 2.5mg;allergyStatus=Không" },
            new[] { "Bùi Thị Thử Nghiệm", "1990", "female", "Tiêu chảy nhiều lần, đau quặn bụng", "1 ngày", "painScore=6;associatedSymptoms=Nôn ói;chronicConditions=Không;allergyStatus=Có;allergyFoods=Hải sản;allergyReaction=Ngứa, phù môi" },
            new[] { "Ngô Minh Mẫu", "1975", "male", "Đau khớp gối hai bên, cứng khớp buổi sáng", "2 tháng", "painScore=5;chronicConditions=Gout;medications=Allopurinol 300mg;alcohol=Thỉnh thoảng;allergyStatus=Không" },
            new[] { "Đỗ Thị Demo", "1963", "female", "Mệt mỏi, khát nước, tiểu nhiều", "1 tháng", "painScore=0;chronicConditions=Đái tháo đường;medications=Gliclazide 30mg;familyConditions=Đái tháo đường;allergyStatus=Không" },
            new[] { "Lý Văn Thử", "1996", "male", "Đau hố chậu phải, sốt nhẹ", "1 ngày", "painScore=8;associatedSymptoms=Buồn nôn, Chán ăn;chronicConditions=Không;allergyStatus=Không" },
            new[] { "Trịnh Thị Mẫu", "1988", "female", "Đau đầu nửa đầu, sợ ánh sáng", "2 ngày", "painScore=7;associatedSymptoms=Buồn nôn;pregnancy=Có;lastMenstrualPeriod=2026-06-20;allergyStatus=Không" },
            new[] { "Mai Văn Kiểm Thử", "1971", "male", "Tiểu buốt, tiểu lắt nhắt, đau hông lưng", "4 ngày", "painScore=6;chronicConditions=Sỏi thận;allergyStatus=Có;allergyDrugs=Sulfamethoxazole;allergyReaction=Phát ban" },
            new[] { "Huỳnh Thị Ví Dụ", "1953", "female", "Đau ngực, hồi hộp, khó thở", "6 giờ", "painScore=7;chronicConditions=Rối loạn lipid máu, Tăng huyết áp;medications=Atorvastatin 20mg;allergyStatus=Không;familyConditions=Bệnh mạch vành" }
        };

        private static readonly string[][] Vitals =
        {
            new[] { "88", "37.2", "150/90", "20", "97", "72", "168" },
            new[] { "96", "38.1", "120/70", "18", "98", "56", "158" },
            new[] { "84", "37.6", "115/75", "22", "96", "60", "170" },
            new[] { "76", "36.8", "160/95", "18", "97", "54", "152" },
            new[] { "80", "36.9", "125/80", "18", "98", "70", "172" },
            new[] { "104", "39.2", "110/70", "20", "97", "48", "160" },
            new[] { "92", "36.7", "140/85", "24", "92", "65", "165" }
        };

        /// <summary>Writes the sample set into the store; returns how many records were created.</summary>
        public static int Seed(IntakeStore store, string actor)
        {
            var now = DateTimeOffset.Now;
            var created = 0;
            for (var i = 0; i < Patients.Length; i++)
            {
                var p = Patients[i];
                var record = new IntakeRecord
                {
                    Source = i % 3 == 0 ? "internet" : "lan",
                    FormVersion = "demo",
                    Patient = new PatientInfo
                    {
                        FullName = p[0],
                        BirthDate = p[1],
                        BirthYear = int.Parse(p[1], CultureInfo.InvariantCulture),
                        Gender = p[2],
                        Phone = "09000000" + (i + 10).ToString(CultureInfo.InvariantCulture),
                        FilledBy = i % 5 == 4 ? "relative" : "self",
                        Relation = i % 5 == 4 ? "Con" : string.Empty
                    },
                    Answers = Answers(p),
                    Review = new ReviewInfo { Fields = new Dictionary<string, string>() },
                    Agent = new AgentInfo(),
                    History = new List<HistoryEntry>()
                };

                // Spread the records over the last 7 days and walk them through the workflow.
                DateTimeOffset submitted;
                string status;
                if (i < 5) { status = IntakeStatus.Pending; submitted = now.AddMinutes(-(8 + i * 37)); }
                else if (i < 8) { status = IntakeStatus.Approved; submitted = now.AddHours(-(2 + (i - 5) * 5)); }
                else if (i == 8) { status = IntakeStatus.Claimed; submitted = now.AddHours(-3); }
                else if (i < 12) { status = IntakeStatus.Completed; submitted = now.AddDays(-(1 + (i - 9) * 2)).AddHours(-3); }
                else { status = IntakeStatus.Rejected; submitted = now.AddDays(-(i - 11)).AddHours(-1); }

                record.CreatedAt = Stamp(submitted);
                record.History.Add(Entry(submitted, record.Source == "internet" ? "patient-internet" : "patient-lan", "submitted", record.Source));

                var t = submitted;
                if (status != IntakeStatus.Pending && status != IntakeStatus.Rejected)
                {
                    t = t.AddMinutes(6 + i);
                    var v = Vitals[i % Vitals.Length];
                    record.Review.HisPatientId = "BN-TEST-0" + (10 + i).ToString(CultureInfo.InvariantCulture);
                    record.Review.Fields = HisFields(p, v);
                    record.Review.ReviewedBy = "Điều dưỡng Mẫu";
                    record.Review.ReviewedAt = Stamp(t);
                    record.Review.ApprovedBy = "Điều dưỡng Mẫu";
                    record.Review.ApprovedAt = Stamp(t);
                    record.History.Add(Entry(t, "dieuduong", "approved", "Mã BN " + record.Review.HisPatientId));
                }
                if (status == IntakeStatus.Claimed || status == IntakeStatus.Completed)
                {
                    t = t.AddMinutes(12 + i);
                    record.Agent.DeviceId = "demo-device";
                    record.Agent.DeviceName = "PK-NGOAI-01";
                    record.Agent.ClaimedAt = Stamp(t);
                    record.Agent.ObservedHisPatientId = record.Review.HisPatientId;
                    record.History.Add(Entry(t, "PK-NGOAI-01", "claimed", null));
                }
                if (status == IntakeStatus.Completed)
                {
                    t = t.AddMinutes(4);
                    record.Agent.CompletedAt = Stamp(t);
                    record.Agent.Result = "completed";
                    record.Agent.FilledCount = 9 + (i % 3);
                    record.Agent.Summary = "Bác sĩ xác nhận đã kiểm tra và lưu trên HIS.";
                    record.History.Add(Entry(t, "PK-NGOAI-01", "completed", record.Agent.FilledCount + " trường"));
                }
                if (status == IntakeStatus.Rejected)
                {
                    t = t.AddMinutes(15);
                    record.Review.RejectedBy = "Điều dưỡng Mẫu";
                    record.Review.RejectedAt = Stamp(t);
                    record.Review.RejectReason = i == 12
                        ? "Yêu cầu bổ sung: chưa ghi tên thuốc đang dùng và liều lượng. Vui lòng khai lại tại quầy."
                        : "Tờ khai trùng với tờ khai đã duyệt của cùng người bệnh.";
                    record.History.Add(Entry(t, "dieuduong", "rejected", record.Review.RejectReason));
                }
                record.Status = status;
                record.UpdatedAt = Stamp(t);
                store.Import(record);
                created++;
            }
            Logs.Audit(actor, "demo-seed", created.ToString(CultureInfo.InvariantCulture), "-");
            return created;
        }

        private static Dictionary<string, object> Answers(string[] p)
        {
            var answers = new Dictionary<string, object>
            {
                { "chiefComplaint", p[3] },
                { "onsetValue", p[4].Split(' ')[0] },
                { "onsetUnit", p[4].Split(' ')[1] },
                { "symptomDescription", p[3] + ", tăng dần trong " + p[4] + "." }
            };
            foreach (var pair in p[5].Split(';'))
            {
                var eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                var key = pair.Substring(0, eq).Trim();
                var value = pair.Substring(eq + 1).Trim();
                answers[key] = value.Contains(",") && (key == "chronicConditions" || key == "associatedSymptoms" || key == "familyConditions")
                    ? (object)value.Split(',').Select(s => s.Trim()).ToList()
                    : value;
            }
            return answers;
        }

        private static Dictionary<string, string> HisFields(string[] p, string[] v)
        {
            var extra = p[5];
            var chronic = Extract(extra, "chronicConditions");
            var meds = Extract(extra, "medications");
            var allergy = Extract(extra, "allergyStatus") == "Có"
                ? "Dị ứng " + string.Join(", ", new[] { Extract(extra, "allergyDrugs"), Extract(extra, "allergyFoods") }.Where(s => s.Length > 0).ToArray()) +
                  (Extract(extra, "allergyReaction").Length > 0 ? " (" + Extract(extra, "allergyReaction") + ")" : string.Empty)
                : "Chưa ghi nhận dị ứng thuốc, thức ăn.";
            var family = Extract(extra, "familyConditions");
            return new Dictionary<string, string>
            {
                { "ReasonForAdmission", p[3] },
                { "History", "Bệnh khởi phát cách nhập viện " + p[4] + " với " + p[3].ToLowerInvariant() + ". Đau mức " + Extract(extra, "painScore") + "/10." +
                    (Extract(extra, "priorTreatmentDetail").Length > 0 ? " Đã " + Extract(extra, "priorTreatmentDetail").ToLowerInvariant() + ", không giảm." : string.Empty) +
                    " Người bệnh đến khám tại " + "Phòng khám." },
                { "PastHistory", (chronic.Length > 0 && chronic != "Không" ? chronic + ". " : "Chưa ghi nhận bệnh mạn tính. ") + (meds.Length > 0 ? "Thuốc đang dùng: " + meds + "." : string.Empty) },
                { "FamilyHistory", family.Length > 0 ? "Gia đình có người mắc " + family.ToLowerInvariant() + "." : "Chưa ghi nhận bệnh lý liên quan." },
                { "Allergy", allergy },
                { "Symptoms", p[3] },
                { "Pulse", v[0] }, { "Temperature", v[1] }, { "BloodPressure", v[2] }, { "RespiratoryRate", v[3] },
                { "SpO2", v[4] }, { "Weight", v[5] }, { "Height", v[6] }
            };
        }

        private static string Extract(string extra, string key)
        {
            foreach (var pair in extra.Split(';'))
            {
                if (pair.StartsWith(key + "=", StringComparison.Ordinal)) return pair.Substring(key.Length + 1).Trim();
            }
            return string.Empty;
        }

        private static HistoryEntry Entry(DateTimeOffset at, string actor, string action, string detail)
        {
            return new HistoryEntry { At = Stamp(at), Actor = actor, Action = action, Detail = detail ?? string.Empty };
        }

        private static string Stamp(DateTimeOffset at)
        {
            return at.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
        }
    }
}
