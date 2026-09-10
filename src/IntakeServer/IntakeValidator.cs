using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Umc2.IntakeServer
{
    /// <summary>Validates and sanitises everything that arrives from the internet-facing patient form or staff edits.</summary>
    internal static class IntakeValidator
    {
        public const int MaxPublicBody = 64 * 1024;
        private static readonly Regex KeyPattern = new Regex("^[A-Za-z][A-Za-z0-9_]{0,39}$", RegexOptions.Compiled);
        private static readonly Regex HisIdPattern = new Regex(@"^[A-Za-z0-9][A-Za-z0-9\-_./]{0,29}$", RegexOptions.Compiled);
        private static readonly Regex DatePattern = new Regex(@"^(\d{4})(?:-(\d{2})-(\d{2}))?$", RegexOptions.Compiled);

        public static IntakeRecord FromPublic(IDictionary<string, object> body, string source)
        {
            if (Json.Str(body, "website").Length > 0)
                throw new ApiException(400, "rejected", "Không gửi được tờ khai.");
            if (!Json.Bool(body, "consent"))
                throw new ApiException(400, "consent_required", "Cần đồng ý cho bệnh viện sử dụng thông tin để khám chữa bệnh.");

            var p = Json.Obj(body, "patient");
            var patient = new PatientInfo
            {
                FullName = TextUtil.Clean(Json.Str(p, "fullName"), 80, false),
                Gender = Json.Str(p, "gender").ToLowerInvariant(),
                Phone = NormalizePhone(Json.Str(p, "phone")),
                NationalId = TextUtil.Digits(Json.Str(p, "nationalId")),
                HisPatientId = NormalizeHisId(Json.Str(p, "hisPatientId"), false),
                FilledBy = Json.Str(p, "filledBy") == "relative" ? "relative" : "self",
                Relation = TextUtil.Clean(Json.Str(p, "relation"), 40, false)
            };
            if (patient.FullName.Length < 2 || !patient.FullName.Any(char.IsLetter))
                throw new ApiException(400, "invalid_name", "Vui lòng nhập họ và tên người bệnh.");
            if (patient.Gender != "male" && patient.Gender != "female" && patient.Gender != "other")
                throw new ApiException(400, "invalid_gender", "Vui lòng chọn giới tính.");
            if (patient.Phone.Length < 9 || patient.Phone.Length > 11)
                throw new ApiException(400, "invalid_phone", "Số điện thoại không hợp lệ.");
            if (patient.NationalId.Length > 0 && patient.NationalId.Length != 9 && patient.NationalId.Length != 12)
                throw new ApiException(400, "invalid_national_id", "Số CCCD/CMND phải có 9 hoặc 12 chữ số.");

            int year;
            patient.BirthDate = NormalizeBirthDate(Json.Str(p, "birthDate"), out year);
            patient.BirthYear = year;

            var record = new IntakeRecord
            {
                Source = source,
                FormVersion = TextUtil.Clean(Json.Str(body, "formVersion"), 20, false),
                Patient = patient,
                Answers = SanitizeAnswers(Json.Obj(body, "answers"))
            };
            object chief;
            if (!record.Answers.TryGetValue("chiefComplaint", out chief) || string.IsNullOrWhiteSpace(Convert.ToString(chief)))
                throw new ApiException(400, "missing_complaint", "Vui lòng cho biết lý do đến khám.");
            if (Json.Serialize(record).Length > MaxPublicBody)
                throw new ApiException(413, "too_large", "Tờ khai quá dài.");
            return record;
        }

        public static Dictionary<string, string> SanitizeFields(IDictionary<string, object> source)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (source == null) return result;
            if (source.Count > 60) throw new ApiException(400, "too_many_fields", "Quá nhiều trường dữ liệu.");
            foreach (var pair in source)
            {
                if (!KeyPattern.IsMatch(pair.Key)) throw new ApiException(400, "invalid_field", "Tên trường không hợp lệ: " + pair.Key);
                if (string.Equals(pair.Key, "PatientId", StringComparison.OrdinalIgnoreCase)) continue; // identity is set via hisPatientId only
                var value = pair.Value == null ? string.Empty : Convert.ToString(pair.Value, CultureInfo.InvariantCulture);
                value = TextUtil.Clean(value, 4000, true);
                if (value.Length > 0) result[pair.Key] = value;
            }
            return result;
        }

        public static string NormalizeHisId(string value, bool strict)
        {
            var trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0) return string.Empty;
            if (!HisIdPattern.IsMatch(trimmed))
            {
                if (strict) throw new ApiException(400, "invalid_his_id", "Mã bệnh nhân chỉ gồm chữ, số và - _ . / (tối đa 30 ký tự).");
                return string.Empty;
            }
            return trimmed.ToUpperInvariant();
        }

        private static string NormalizePhone(string value)
        {
            var digits = TextUtil.Digits(value);
            if (digits.StartsWith("84") && digits.Length >= 11) digits = "0" + digits.Substring(2);
            return digits;
        }

        private static string NormalizeBirthDate(string value, out int year)
        {
            var match = DatePattern.Match((value ?? string.Empty).Trim());
            if (!match.Success) throw new ApiException(400, "invalid_birth_date", "Vui lòng nhập ngày sinh (hoặc năm sinh).");
            year = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var now = DateTime.Now;
            if (year < 1900 || year > now.Year) throw new ApiException(400, "invalid_birth_date", "Năm sinh không hợp lệ.");
            if (!match.Groups[2].Success) return year.ToString(CultureInfo.InvariantCulture);
            DateTime date;
            var text = match.Value;
            if (!DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date) || date > now)
                throw new ApiException(400, "invalid_birth_date", "Ngày sinh không hợp lệ.");
            return text;
        }

        private static Dictionary<string, object> SanitizeAnswers(IDictionary<string, object> source)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            if (source.Count > 80) throw new ApiException(400, "too_many_answers", "Tờ khai có quá nhiều mục.");
            foreach (var pair in source)
            {
                if (!KeyPattern.IsMatch(pair.Key)) continue;
                var value = pair.Value;
                if (value == null) continue;
                if (value is string)
                {
                    var text = TextUtil.Clean((string)value, 2000, true);
                    if (text.Length > 0) result[pair.Key] = text;
                }
                else if (value is bool)
                {
                    result[pair.Key] = value;
                }
                else if (value is int || value is long || value is decimal || value is double)
                {
                    var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    if (number > -100000 && number < 100000) result[pair.Key] = value;
                }
                else if (value is IEnumerable && !(value is IDictionary<string, object>))
                {
                    var items = new List<string>();
                    foreach (var item in (IEnumerable)value)
                    {
                        if (item == null || item is IDictionary<string, object>) continue;
                        var text = TextUtil.Clean(Convert.ToString(item, CultureInfo.InvariantCulture), 200, false);
                        if (text.Length > 0 && !items.Contains(text)) items.Add(text);
                        if (items.Count >= 40) break;
                    }
                    if (items.Count > 0) result[pair.Key] = items;
                }
            }
            return result;
        }
    }
}
