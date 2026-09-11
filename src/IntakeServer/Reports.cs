using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Umc2.IntakeServer
{
    /// <summary>Dashboard figures and the Excel export, both computed from the in-memory store (no PHI in logs).</summary>
    internal static class Reports
    {
        public static Dictionary<string, object> Stats(IntakeStore store, int days)
        {
            var now = DateTimeOffset.Now;
            var since = now.Date.AddDays(-(days - 1));
            var all = store.Select(r => true, r => r);
            var window = all.Where(r => TextUtil.ParseTime(r.CreatedAt) >= since).ToList();
            var today = all.Where(r => TextUtil.ParseTime(r.CreatedAt).Date == now.Date).ToList();

            var perDay = new List<Dictionary<string, object>>();
            for (var i = 0; i < days; i++)
            {
                var day = since.AddDays(i);
                var ofDay = window.Where(r => TextUtil.ParseTime(r.CreatedAt).Date == day).ToList();
                perDay.Add(new Dictionary<string, object>
                {
                    { "date", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
                    { "label", day.ToString("dd/MM", CultureInfo.InvariantCulture) },
                    { "submitted", ofDay.Count },
                    { "completed", ofDay.Count(r => r.Status == IntakeStatus.Completed) },
                    { "rejected", ofDay.Count(r => r.Status == IntakeStatus.Rejected) }
                });
            }

            var approveMinutes = window
                .Where(r => r.Review.ApprovedAt.Length > 0)
                .Select(r => Minutes(r.CreatedAt, r.Review.ApprovedAt)).Where(m => m >= 0).ToList();
            var hisMinutes = window
                .Where(r => r.Agent.CompletedAt.Length > 0 && r.Review.ApprovedAt.Length > 0)
                .Select(r => Minutes(r.Review.ApprovedAt, r.Agent.CompletedAt)).Where(m => m >= 0).ToList();
            var complaints = window
                .Select(r => TextUtil.FirstLine(Views.AnswerText(r, "chiefComplaint"), 60))
                .Where(s => s.Length > 0)
                .GroupBy(s => TextUtil.FoldName(s))
                .Select(g => new Dictionary<string, object> { { "text", g.First() }, { "count", g.Count() } })
                .OrderByDescending(d => (int)d["count"]).Take(6).ToList();
            var devices = window
                .Where(r => r.Agent.CompletedAt.Length > 0 && r.Agent.DeviceName.Length > 0)
                .GroupBy(r => r.Agent.DeviceName)
                .Select(g => new Dictionary<string, object> { { "device", g.Key }, { "completed", g.Count() }, { "fields", g.Sum(r => r.Agent.FilledCount) } })
                .OrderByDescending(d => (int)d["completed"]).Take(8).ToList();

            return new Dictionary<string, object>
            {
                { "days", days },
                { "generatedAt", TextUtil.Now() },
                { "total", all.Count },
                { "counts", store.Counts() },
                { "today", new Dictionary<string, object>
                    {
                        { "submitted", today.Count },
                        { "approved", today.Count(r => r.Review.ApprovedAt.Length > 0) },
                        { "completed", today.Count(r => r.Status == IntakeStatus.Completed) },
                        { "internet", today.Count(r => r.Source == "internet") }
                    } },
                { "window", new Dictionary<string, object>
                    {
                        { "submitted", window.Count },
                        { "completed", window.Count(r => r.Status == IntakeStatus.Completed) },
                        { "rejected", window.Count(r => r.Status == IntakeStatus.Rejected) },
                        { "internet", window.Count(r => r.Source == "internet") },
                        { "fieldsFilled", window.Sum(r => r.Agent.FilledCount) },
                        { "avgApproveMinutes", approveMinutes.Count == 0 ? 0 : Math.Round(approveMinutes.Average(), 1) },
                        { "medianApproveMinutes", Median(approveMinutes) },
                        { "avgHisMinutes", hisMinutes.Count == 0 ? 0 : Math.Round(hisMinutes.Average(), 1) },
                        { "medianHisMinutes", Median(hisMinutes) }
                    } },
                { "perDay", perDay },
                { "complaints", complaints },
                { "devices", devices }
            };
        }

        public static byte[] ExportXlsx(IntakeStore store, int days, string status)
        {
            var since = DateTimeOffset.Now.Date.AddDays(-(days - 1));
            var records = store.Select(
                r => TextUtil.ParseTime(r.CreatedAt) >= since && (status.Length == 0 || r.Status == status),
                r => r).OrderByDescending(r => TextUtil.ParseTime(r.CreatedAt)).ToList();
            var headers = new List<string>
            {
                "Mã tờ khai", "Trạng thái", "Nguồn", "Gửi lúc", "Họ tên", "Năm sinh", "Giới", "Điện thoại", "Mã BN (ĐD xác nhận)",
                "Lý do khám", "Khởi phát", "Dị ứng", "Bệnh nền", "Mạch", "Nhiệt độ", "Huyết áp", "Nhịp thở", "SpO2", "Cân nặng", "Chiều cao",
                "Duyệt bởi", "Duyệt lúc", "Phút chờ duyệt", "Máy bác sĩ", "Nhập HIS lúc", "Phút đến HIS", "Số trường điền", "Lý do từ chối"
            };
            var rows = new List<IList<string>>();
            foreach (var r in records)
            {
                var f = r.Review.Fields;
                rows.Add(new List<string>
                {
                    r.Code, StatusLabel(r.Status), r.Source == "internet" ? "Internet" : "Tại viện", Local(r.CreatedAt),
                    r.Patient.FullName, r.Patient.BirthYear > 0 ? r.Patient.BirthYear.ToString(CultureInfo.InvariantCulture) : string.Empty,
                    r.Patient.Gender == "male" ? "Nam" : r.Patient.Gender == "female" ? "Nữ" : "Khác", r.Patient.Phone, r.Review.HisPatientId,
                    Views.AnswerText(r, "chiefComplaint"), (Views.AnswerText(r, "onsetValue") + " " + Views.AnswerText(r, "onsetUnit")).Trim(),
                    Views.AnswerText(r, "allergyStatus") == "Có" ? string.Join(", ", new[] { Views.AnswerText(r, "allergyDrugs"), Views.AnswerText(r, "allergyFoods"), Views.AnswerText(r, "allergyOther") }.Where(s => s.Length > 0).ToArray()) : Views.AnswerText(r, "allergyStatus"),
                    Views.AnswerText(r, "chronicConditions"),
                    Field(f, "Pulse"), Field(f, "Temperature"), Field(f, "BloodPressure"), Field(f, "RespiratoryRate"), Field(f, "SpO2"), Field(f, "Weight"), Field(f, "Height"),
                    r.Review.ApprovedBy, Local(r.Review.ApprovedAt), r.Review.ApprovedAt.Length > 0 ? Minutes(r.CreatedAt, r.Review.ApprovedAt).ToString("0", CultureInfo.InvariantCulture) : string.Empty,
                    r.Agent.DeviceName, Local(r.Agent.CompletedAt), r.Agent.CompletedAt.Length > 0 && r.Review.ApprovedAt.Length > 0 ? Minutes(r.Review.ApprovedAt, r.Agent.CompletedAt).ToString("0", CultureInfo.InvariantCulture) : string.Empty,
                    r.Agent.FilledCount > 0 ? r.Agent.FilledCount.ToString(CultureInfo.InvariantCulture) : string.Empty, r.Review.RejectReason
                });
            }
            return XlsxWriter.Build("To khai", headers, rows);
        }

        public static string StatusLabel(string status)
        {
            switch (status)
            {
                case IntakeStatus.Pending: return "Chờ duyệt";
                case IntakeStatus.Approved: return "Chờ bác sĩ";
                case IntakeStatus.Claimed: return "Bác sĩ đang điền";
                case IntakeStatus.Completed: return "Đã nhập HIS";
                case IntakeStatus.Rejected: return "Từ chối / yêu cầu bổ sung";
                default: return status;
            }
        }

        private static string Field(IDictionary<string, string> fields, string key)
        {
            string value;
            return fields != null && fields.TryGetValue(key, out value) ? value : string.Empty;
        }

        private static string Local(string stamp)
        {
            if (string.IsNullOrEmpty(stamp)) return string.Empty;
            var t = TextUtil.ParseTime(stamp);
            return t == DateTimeOffset.MinValue ? string.Empty : t.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        }

        private static double Minutes(string from, string to)
        {
            var a = TextUtil.ParseTime(from);
            var b = TextUtil.ParseTime(to);
            if (a == DateTimeOffset.MinValue || b == DateTimeOffset.MinValue) return -1;
            return Math.Round((b - a).TotalMinutes, 1);
        }

        private static double Median(List<double> values)
        {
            if (values.Count == 0) return 0;
            var sorted = values.OrderBy(v => v).ToList();
            var mid = sorted.Count / 2;
            return sorted.Count % 2 == 1 ? sorted[mid] : Math.Round((sorted[mid - 1] + sorted[mid]) / 2, 1);
        }
    }
}
