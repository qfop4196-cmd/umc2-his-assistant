using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Web.Script.Serialization;

namespace Umc2.IntakeServer
{
    /// <summary>JSON helpers on top of JavaScriptSerializer (.NET 4.0, no NuGet).</summary>
    internal static class Json
    {
        private static JavaScriptSerializer Create()
        {
            return new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024, RecursionLimit = 40 };
        }

        public static string Serialize(object value)
        {
            return Create().Serialize(value);
        }

        public static T Deserialize<T>(string text)
        {
            return Create().Deserialize<T>(text);
        }

        public static Dictionary<string, object> ParseObject(string text)
        {
            object parsed;
            try { parsed = Create().DeserializeObject(text); }
            catch (Exception) { throw new ApiException(400, "invalid_json", "Dữ liệu gửi lên không phải JSON hợp lệ."); }
            var result = parsed as Dictionary<string, object>;
            if (result == null) throw new ApiException(400, "invalid_json", "Dữ liệu gửi lên phải là một đối tượng JSON.");
            return result;
        }

        public static IDictionary<string, object> Obj(IDictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null) return new Dictionary<string, object>();
            var dictionary = value as IDictionary<string, object>;
            return dictionary ?? new Dictionary<string, object>();
        }

        public static string Str(IDictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null) return string.Empty;
            if (value is string) return ((string)value).Trim();
            if (value is bool) return (bool)value ? "true" : "false";
            if (value is IDictionary<string, object> || (value is IEnumerable && !(value is string))) return string.Empty;
            return Convert.ToString(value, CultureInfo.InvariantCulture).Trim();
        }

        public static bool Bool(IDictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null) return false;
            if (value is bool) return (bool)value;
            var text = Convert.ToString(value, CultureInfo.InvariantCulture);
            return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) || text == "1";
        }

        public static int Int(IDictionary<string, object> source, string key, int fallback)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null) return fallback;
            int parsed;
            if (value is int) return (int)value;
            if (value is long) return (int)(long)value;
            if (value is decimal) return (int)(decimal)value;
            if (value is double) return (int)(double)value;
            return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                ? parsed : fallback;
        }

        public static List<string> StrList(IDictionary<string, object> source, string key)
        {
            var result = new List<string>();
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null) return result;
            var list = value as IEnumerable;
            if (list == null || value is string) return result;
            foreach (var item in list)
            {
                if (item == null) continue;
                var text = Convert.ToString(item, CultureInfo.InvariantCulture).Trim();
                if (text.Length > 0) result.Add(text);
            }
            return result;
        }

        /// <summary>Readable, stable indentation for config files that administrators may edit by hand.</summary>
        public static string Indent(string json)
        {
            var sb = new StringBuilder(json.Length + 256);
            var level = 0;
            var inString = false;
            for (var i = 0; i < json.Length; i++)
            {
                var c = json[i];
                if (inString)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < json.Length) { sb.Append(json[++i]); continue; }
                    if (c == '"') inString = false;
                    continue;
                }
                switch (c)
                {
                    case '"': inString = true; sb.Append(c); break;
                    case '{':
                    case '[':
                        sb.Append(c);
                        if (i + 1 < json.Length && (json[i + 1] == '}' || json[i + 1] == ']')) { sb.Append(json[++i]); break; }
                        level++;
                        sb.Append("\r\n").Append(' ', level * 2);
                        break;
                    case '}':
                    case ']':
                        level = Math.Max(0, level - 1);
                        sb.Append("\r\n").Append(' ', level * 2).Append(c);
                        break;
                    case ',': sb.Append(c).Append("\r\n").Append(' ', level * 2); break;
                    case ':': sb.Append(": "); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }

    internal sealed class ApiException : Exception
    {
        public ApiException(int status, string code, string message) : base(message)
        {
            Status = status;
            Code = code;
        }

        public int Status { get; private set; }
        public string Code { get; private set; }
    }
}
