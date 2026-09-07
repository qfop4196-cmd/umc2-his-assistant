using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace HisAdmissionAssistant
{
    public sealed class CloudQueueClient
    {
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };

        public CloudAutomationJob Claim(string baseUrl, string secret, string agentId)
        {
            var response = Post(baseUrl, "/api/agent/jobs/claim", secret, new Dictionary<string, object> { { "agentId", agentId } });
            object jobObject;
            if (!response.TryGetValue("job", out jobObject) || jobObject == null) return null;
            var job = jobObject as IDictionary<string, object>;
            if (job == null) throw new InvalidOperationException("Phản hồi hàng đợi không hợp lệ.");
            var payload = GetDictionary(job, "payload");
            var form = GetDictionary(payload, "form");
            var fieldsObject = GetDictionary(payload, "fields");
            var result = new CloudAutomationJob
            {
                Id = GetString(job, "id"),
                AdmissionId = GetString(job, "admissionId"),
                FormCode = GetString(form, "code"),
                FormName = GetString(form, "name"),
                SignaturePolicy = GetString(form, "signaturePolicy")
            };
            foreach (var pair in fieldsObject)
                result.Fields[pair.Key] = pair.Value == null ? string.Empty : Convert.ToString(pair.Value);
            return result;
        }

        public CloudAutomationJob LoadPilotPackage(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException("Không tìm thấy gói pilot.", path);
            var root = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
            if (GetString(root, "format") != "umc2-his-pilot-v1")
                throw new InvalidOperationException("Định dạng gói pilot không được hỗ trợ.");
            var source = GetDictionary(root, "job");
            var result = new CloudAutomationJob
            {
                Id = GetString(source, "id"),
                AdmissionId = GetString(source, "admissionId"),
                FormCode = GetString(source, "formCode"),
                FormName = GetString(source, "formName"),
                SignaturePolicy = GetString(source, "signaturePolicy"),
                IsOffline = true
            };
            foreach (var pair in GetDictionary(source, "fields"))
                result.Fields[pair.Key] = pair.Value == null ? string.Empty : Convert.ToString(pair.Value);
            if (string.IsNullOrWhiteSpace(result.Id) || string.IsNullOrWhiteSpace(result.FormCode) || result.Fields.Count == 0)
                throw new InvalidOperationException("Gói pilot thiếu mã biểu mẫu hoặc dữ liệu trường.");
            return result;
        }

        public void Complete(string baseUrl, string secret, string agentId, CloudAutomationJob job, string status, string summary)
        {
            Post(baseUrl, "/api/agent/jobs/complete", secret, new Dictionary<string, object>
            {
                { "jobId", job.Id },
                { "agentId", agentId },
                { "status", status },
                { "result", new Dictionary<string, object> { { "summary", summary } } }
            });
        }

        private IDictionary<string, object> Post(string baseUrl, string path, string secret, object body)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) throw new InvalidOperationException("Chưa cấu hình địa chỉ webapp.");
            if (string.IsNullOrWhiteSpace(secret)) throw new InvalidOperationException("Chưa cấu hình khóa tác nhân HIS.");
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            var request = (HttpWebRequest)WebRequest.Create(baseUrl.TrimEnd('/') + path);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Accept = "application/json";
            request.Headers[HttpRequestHeader.Authorization] = "Bearer " + secret;
            request.Timeout = 15000;
            var bytes = Encoding.UTF8.GetBytes(json.Serialize(body));
            request.ContentLength = bytes.Length;
            using (var stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream()))
                    return json.Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
            }
            catch (WebException ex)
            {
                var response = ex.Response as HttpWebResponse;
                if (response == null) throw;
                using (response)
                using (var reader = new StreamReader(response.GetResponseStream()))
                    throw new InvalidOperationException("Webapp từ chối tác vụ (HTTP " + (int)response.StatusCode + "): " + reader.ReadToEnd());
            }
        }

        private static IDictionary<string, object> GetDictionary(IDictionary<string, object> source, string key)
        {
            object value;
            if (!source.TryGetValue(key, out value) || value == null) return new Dictionary<string, object>();
            var dictionary = value as IDictionary<string, object>;
            return dictionary ?? new Dictionary<string, object>();
        }

        private static string GetString(IDictionary<string, object> source, string key)
        {
            object value;
            return source.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : string.Empty;
        }
    }
}
