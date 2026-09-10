using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Umc2.IntakeServer
{
    internal static class TextUtil
    {
        private static readonly Regex MultiSpace = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly Regex NonAlnum = new Regex("[^0-9A-Za-z]", RegexOptions.Compiled);

        public static string Now()
        {
            return DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
        }

        public static DateTimeOffset ParseTime(string value)
        {
            DateTimeOffset parsed;
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)
                ? parsed : DateTimeOffset.MinValue;
        }

        /// <summary>Removes Vietnamese diacritics, lower-cases and collapses whitespace: "Nguyễn  Văn Ánh" -> "nguyen van anh".</summary>
        public static string FoldName(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var decomposed = value.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            foreach (var c in decomposed)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return MultiSpace.Replace(sb.ToString().Normalize(NormalizationForm.FormC), " ").Trim().ToLowerInvariant();
        }

        /// <summary>Normalises patient identifiers for comparison: keeps letters and digits only, upper-case.</summary>
        public static string FoldId(string value)
        {
            return NonAlnum.Replace(value ?? string.Empty, string.Empty).ToUpperInvariant();
        }

        /// <summary>Trims, removes control characters (except newlines/tabs) and enforces a maximum length.</summary>
        public static string Clean(string value, int maxLength, bool multiline)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var sb = new StringBuilder(Math.Min(value.Length, maxLength));
            foreach (var c in value)
            {
                if (c == '\r') continue;
                if (c == '\n' || c == '\t')
                {
                    sb.Append(multiline ? c : ' ');
                    continue;
                }
                if (char.IsControl(c)) continue;
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format && c != '\u200D') continue;
                sb.Append(c);
            }
            var result = sb.ToString().Trim();
            if (!multiline) result = MultiSpace.Replace(result, " ");
            if (result.Length > maxLength) throw new ApiException(400, "too_long", "Nội dung quá dài (tối đa " + maxLength + " ký tự).");
            return result.Normalize(NormalizationForm.FormC);
        }

        public static string Digits(string value)
        {
            return new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        }

        public static string MaskPhone(string phone)
        {
            var digits = Digits(phone);
            if (digits.Length < 7) return digits.Length == 0 ? string.Empty : "***";
            return digits.Substring(0, 3) + new string('*', digits.Length - 6) + digits.Substring(digits.Length - 3);
        }

        public static string FirstLine(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var line = value.Split('\n')[0].Trim();
            return line.Length <= max ? line : line.Substring(0, max - 1) + "…";
        }
    }
}
