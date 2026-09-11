using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace Umc2.IntakeServer
{
    /// <summary>
    /// Controlled AI helper for the nurse page (Anthropic Messages API, or any server speaking the same protocol —
    /// the endpoint is configurable, which is also how the flow test mocks it).
    ///
    /// Control points, on purpose:
    ///  * the model only ever sees the clinical answers of ONE record, de-identified (age, gender, answers, vitals —
    ///    never name, phone, national ID or HIS code);
    ///  * every answer is a strict JSON contract validated and length-clipped here; free text never reaches HIS directly;
    ///  * drafts are suggestions the nurse accepts field by field, edits, or rejects — and each decision is audited;
    ///  * questions outside the record ("out of scope") must be refused by the model and are flagged to the UI.
    /// </summary>
    internal sealed class AiAssistant
    {
        public const string DefaultEndpoint = "https://api.anthropic.com/v1/messages";
        public const string DefaultModel = "claude-sonnet-4-5";
        private static readonly string[] DraftKeys = { "ReasonForAdmission", "History", "PastHistory", "FamilyHistory", "Allergy", "Symptoms", "PreliminaryDiagnosis" };
        private readonly ServerHost host;

        static AiAssistant()
        {
            // .NET Framework 4.0 targets: TLS 1.2 (0xC00) is not in the enum but is honoured by the 4.5+ runtime.
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)0xC00 | (SecurityProtocolType)0x300; }
            catch (NotSupportedException) { }
        }

        public AiAssistant(ServerHost host)
        {
            this.host = host;
        }

        public bool Enabled
        {
            get { return host.Config.Read(c => c.AiEnabled && c.AiApiKeyProtected.Length > 0); }
        }

        /// <summary>Drafts the doctor-facing text fields from the intake answers; returns per-field suggestions plus red flags.</summary>
        public Dictionary<string, object> Draft(IntakeRecord record)
        {
            const string system =
                "Bạn là trợ lý soạn thảo cho điều dưỡng phòng khám. Bạn CHỈ dùng dữ liệu tờ khai trong tin nhắn; không bịa thêm chi tiết. " +
                "Thông tin thiếu thì ghi \"chưa khai\". Không đưa chẩn đoán xác định: chỉ nêu chẩn đoán sơ bộ GỢI Ý để bác sĩ cân nhắc, kèm mã ICD-10 gợi ý. " +
                "Liệt kê dấu hiệu nguy hiểm (red flags) cần ưu tiên nếu có. Văn phong hồ sơ bệnh án, tiếng Việt, ngắn gọn, không xưng hô. " +
                "Trả về DUY NHẤT một đối tượng JSON với các khóa: ReasonForAdmission, History, PastHistory, FamilyHistory, Allergy, Symptoms, " +
                "PreliminaryDiagnosis (chuỗi), IcdSuggestions (mảng chuỗi \"mã - tên\"), RedFlags (mảng chuỗi), MissingInfo (mảng chuỗi), " +
                "Confidence (\"cao\"|\"trung bình\"|\"thấp\"). Không thêm chữ nào ngoài JSON.";
            var text = Complete(system, "DỮ LIỆU TỜ KHAI (đã bỏ định danh):\n" + Describe(record) + "\n\nHãy soạn bản nháp theo đúng mẫu JSON.", 1400);
            var json = ParseJson(text);
            var fields = new Dictionary<string, string>();
            foreach (var key in DraftKeys)
            {
                var value = TextUtil.Clean(Json.Str(json, key), 2000, true);
                if (value.Length > 0 && value != "chưa khai") fields[key] = value;
            }
            if (fields.Count == 0) throw new ApiException(502, "ai_empty", "AI không trả về nội dung dùng được. Hãy thử lại hoặc soạn tay.");
            return new Dictionary<string, object>
            {
                { "fields", fields },
                { "icd", Json.StrList(json, "IcdSuggestions").Select(s => TextUtil.Clean(s, 120, false)).Where(s => s.Length > 0).Take(5).ToList() },
                { "redFlags", Json.StrList(json, "RedFlags").Select(s => TextUtil.Clean(s, 200, false)).Where(s => s.Length > 0).Take(8).ToList() },
                { "missing", Json.StrList(json, "MissingInfo").Select(s => TextUtil.Clean(s, 200, false)).Where(s => s.Length > 0).Take(8).ToList() },
                { "confidence", TextUtil.Clean(Json.Str(json, "Confidence"), 20, false) },
                { "model", host.Config.Read(c => c.AiModel) },
                { "disclaimer", "Bản nháp do AI soạn từ câu trả lời của người bệnh. Điều dưỡng kiểm tra, sửa hoặc bỏ từng mục; bác sĩ chịu trách nhiệm nội dung cuối cùng trên HIS." }
            };
        }

        /// <summary>Answers a question about this record only; anything else is refused (inScope = false).</summary>
        public Dictionary<string, object> Ask(IntakeRecord record, string question)
        {
            const string system =
                "Bạn là trợ lý của điều dưỡng, chỉ trả lời câu hỏi VỀ TỜ KHAI được cung cấp (nội dung khai, dị ứng, thuốc, dấu hiệu cần lưu ý, mục còn thiếu). " +
                "Không dùng kiến thức ngoài tờ khai để suy đoán về người bệnh. Không kê đơn, không quyết định điều trị, không trả lời chủ đề ngoài tờ khai " +
                "(thời sự, lập trình, hành chính, đời tư...). Nếu câu hỏi ngoài phạm vi hoặc cần bác sĩ quyết định: đặt inScope=false và answer là một câu " +
                "từ chối lịch sự, gợi ý hỏi bác sĩ. Trả về DUY NHẤT JSON: {\"answer\": chuỗi tiếng Việt ngắn gọn, \"inScope\": true|false}.";
            var text = Complete(system, "DỮ LIỆU TỜ KHAI (đã bỏ định danh):\n" + Describe(record) + "\n\nCÂU HỎI CỦA ĐIỀU DƯỠNG: " + question, 600);
            var json = ParseJson(text);
            var answer = TextUtil.Clean(Json.Str(json, "answer"), 1500, true);
            if (answer.Length == 0) throw new ApiException(502, "ai_empty", "AI không trả lời được. Hãy thử lại.");
            return new Dictionary<string, object>
            {
                { "answer", answer },
                { "inScope", Json.Bool(json, "inScope") },
                { "model", host.Config.Read(c => c.AiModel) }
            };
        }

        /// <summary>Round-trip check used by the settings page; returns the model name reported by the API.</summary>
        public string Test()
        {
            var text = Complete("Trả lời đúng một từ.", "Nói \"sẵn sàng\".", 20);
            return text.Length > 0 ? text : "(trống)";
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>De-identified description: age band, gender, answers, nurse vitals. No name/phone/ID/code.</summary>
        internal static string Describe(IntakeRecord r)
        {
            var sb = new StringBuilder();
            var age = r.Patient.BirthYear > 0 ? (DateTime.Now.Year - r.Patient.BirthYear) + " tuổi" : "tuổi chưa rõ";
            var gender = r.Patient.Gender == "male" ? "nam" : r.Patient.Gender == "female" ? "nữ" : "giới khác";
            sb.Append("- Người bệnh: ").Append(gender).Append(", ").Append(age).Append('\n');
            var labels = new[]
            {
                new[] { "chiefComplaint", "Lý do khám" }, new[] { "onsetValue", "Khởi phát cách đây" }, new[] { "onsetUnit", "Đơn vị thời gian" },
                new[] { "symptomDescription", "Mô tả triệu chứng" }, new[] { "painScore", "Mức độ đau (0-10)" }, new[] { "associatedSymptoms", "Triệu chứng kèm" },
                new[] { "associatedOther", "Triệu chứng khác" }, new[] { "priorTreatment", "Đã điều trị trước" }, new[] { "priorTreatmentDetail", "Chi tiết điều trị" },
                new[] { "chronicConditions", "Bệnh đã/đang mắc" }, new[] { "chronicOther", "Bệnh khác" }, new[] { "surgeries", "Phẫu thuật/thủ thuật" },
                new[] { "medications", "Thuốc đang dùng" }, new[] { "smoking", "Hút thuốc" }, new[] { "alcohol", "Rượu bia" }, new[] { "pregnancy", "Mang thai" },
                new[] { "lastMenstrualPeriod", "Kinh chót" }, new[] { "allergyStatus", "Dị ứng" }, new[] { "allergyDrugs", "Dị ứng thuốc" },
                new[] { "allergyFoods", "Dị ứng thức ăn" }, new[] { "allergyOther", "Dị ứng khác" }, new[] { "allergyReaction", "Biểu hiện dị ứng" },
                new[] { "familyConditions", "Tiền sử gia đình" }, new[] { "familyOther", "Gia đình – bệnh khác" }, new[] { "weightKg", "Cân nặng tự khai (kg)" },
                new[] { "heightCm", "Chiều cao tự khai (cm)" }, new[] { "notes", "Ghi chú của người bệnh" }
            };
            foreach (var pair in labels)
            {
                var value = Views.AnswerText(r, pair[0]);
                if (value.Length > 0) sb.Append("- ").Append(pair[1]).Append(": ").Append(TextUtil.Clean(value, 600, false)).Append('\n');
            }
            var vitals = new[] { "Pulse|Mạch (lần/phút)", "Temperature|Nhiệt độ (°C)", "BloodPressure|Huyết áp (mmHg)", "RespiratoryRate|Nhịp thở (lần/phút)", "SpO2|SpO2 (%)", "Weight|Cân nặng (kg)", "Height|Chiều cao (cm)" };
            var any = false;
            foreach (var v in vitals)
            {
                var parts = v.Split('|');
                string value;
                if (r.Review.Fields != null && r.Review.Fields.TryGetValue(parts[0], out value) && value.Length > 0)
                {
                    if (!any) { sb.Append("- Sinh hiệu điều dưỡng đo: "); any = true; } else sb.Append("; ");
                    sb.Append(parts[1]).Append(' ').Append(value);
                }
            }
            if (any) sb.Append('\n');
            return sb.ToString();
        }

        private string Complete(string system, string user, int maxTokens)
        {
            string endpoint = DefaultEndpoint, model = DefaultModel, keyProtected = string.Empty;
            host.Config.Read(c =>
            {
                if (c.AiEndpoint.Length > 0) endpoint = c.AiEndpoint;
                if (c.AiModel.Length > 0) model = c.AiModel;
                keyProtected = c.AiApiKeyProtected;
                return 0;
            });
            if (keyProtected.Length == 0) throw new ApiException(409, "ai_not_configured", "Chưa cấu hình khóa API cho trợ lý AI (Quản trị → Cài đặt).");
            string apiKey;
            try { apiKey = host.Protector.Unprotect(Convert.FromBase64String(keyProtected)); }
            catch (Exception) { throw new ApiException(500, "ai_key_unreadable", "Không đọc được khóa API đã lưu — hãy nhập lại ở Cài đặt."); }

            var body = Json.Serialize(new Dictionary<string, object>
            {
                { "model", model },
                { "max_tokens", maxTokens },
                { "temperature", 0.2 },
                { "system", system },
                { "messages", new[] { new Dictionary<string, object> { { "role", "user" }, { "content", user } } } }
            });
            var request = (HttpWebRequest)WebRequest.Create(endpoint);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Accept = "application/json";
            request.Headers["x-api-key"] = apiKey;
            request.Headers["anthropic-version"] = "2023-06-01";
            request.Timeout = 60000;
            request.ReadWriteTimeout = 60000;
            request.UserAgent = "UMC2-IntakeServer/" + host.Version;
            var payload = Encoding.UTF8.GetBytes(body);
            try
            {
                request.ContentLength = payload.Length;
                using (var stream = request.GetRequestStream()) stream.Write(payload, 0, payload.Length);
                using (var response = (HttpWebResponse)request.GetResponse())
                    return ExtractText(ReadAll(response.GetResponseStream()));
            }
            catch (WebException ex)
            {
                var http = ex.Response as HttpWebResponse;
                if (http == null) throw new ApiException(502, "ai_unreachable", "Không kết nối được dịch vụ AI: " + ex.Message);
                var detail = ReadAll(http.GetResponseStream());
                string message;
                try
                {
                    var error = Json.Obj(Json.ParseObject(detail), "error");
                    message = Json.Str(error, "message");
                }
                catch (Exception) { message = string.Empty; }
                if (message.Length == 0) message = TextUtil.FirstLine(detail, 200);
                Logs.Warn("AI HTTP " + (int)http.StatusCode + ": " + TextUtil.FirstLine(message, 200));
                throw new ApiException(502, "ai_error", "Dịch vụ AI trả lỗi " + (int)http.StatusCode + ": " + message);
            }
            finally
            {
                apiKey = null;
            }
        }

        private static string ReadAll(Stream stream)
        {
            if (stream == null) return string.Empty;
            using (var reader = new StreamReader(stream, Encoding.UTF8)) return reader.ReadToEnd();
        }

        /// <summary>Anthropic response: {"content":[{"type":"text","text":"..."}], ...}.</summary>
        private static string ExtractText(string responseJson)
        {
            var root = Json.ParseObject(responseJson);
            object contentObj;
            var sb = new StringBuilder();
            if (root.TryGetValue("content", out contentObj) && contentObj is System.Collections.IEnumerable)
            {
                foreach (var item in (System.Collections.IEnumerable)contentObj)
                {
                    var block = item as IDictionary<string, object>;
                    if (block != null && Json.Str(block, "type") == "text") sb.Append(Json.Str(block, "text"));
                }
            }
            return sb.ToString().Trim();
        }

        /// <summary>Accepts the JSON object the model was told to return, tolerating a ```json fence around it.</summary>
        private static Dictionary<string, object> ParseJson(string text)
        {
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start < 0 || end <= start) throw new ApiException(502, "ai_format", "AI trả về không đúng định dạng. Hãy thử lại.");
            try { return Json.ParseObject(text.Substring(start, end - start + 1)); }
            catch (Exception) { throw new ApiException(502, "ai_format", "AI trả về JSON không hợp lệ. Hãy thử lại."); }
        }
    }
}
