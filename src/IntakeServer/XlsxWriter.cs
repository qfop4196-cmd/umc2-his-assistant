using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Umc2.IntakeServer
{
    /// <summary>
    /// Minimal .xlsx writer (one sheet, inline strings, frozen header, auto-filter) with no external library:
    /// the workbook is a ZIP of five XML parts, written here with stored (uncompressed) entries.
    /// .NET Framework 4.0 has no ZipArchive, and System.IO.Packaging adds nothing an export needs.
    /// </summary>
    internal static class XlsxWriter
    {
        public static byte[] Build(string sheetName, IList<string> headers, IList<IList<string>> rows)
        {
            var sheet = new StringBuilder();
            sheet.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sheet.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            var lastCol = ColumnName(Math.Max(1, headers.Count));
            var lastRow = rows.Count + 1;
            sheet.Append("<dimension ref=\"A1:" + lastCol + lastRow.ToString(CultureInfo.InvariantCulture) + "\"/>");
            sheet.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
            sheet.Append("<sheetFormatPr defaultRowHeight=\"15\"/>");
            sheet.Append("<cols><col min=\"1\" max=\"" + headers.Count.ToString(CultureInfo.InvariantCulture) + "\" width=\"20\" customWidth=\"1\"/></cols>");
            sheet.Append("<sheetData>");
            AppendRow(sheet, 1, headers, true);
            for (var i = 0; i < rows.Count; i++) AppendRow(sheet, i + 2, rows[i], false);
            sheet.Append("</sheetData>");
            sheet.Append("<autoFilter ref=\"A1:" + lastCol + lastRow.ToString(CultureInfo.InvariantCulture) + "\"/>");
            sheet.Append("</worksheet>");

            var workbook =
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                "<sheets><sheet name=\"" + Escape(SafeSheetName(sheetName)) + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>";
            const string workbookRels =
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                "</Relationships>";
            const string styles =
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
                "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
                "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                "<cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/></cellXfs>" +
                "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>";
            const string contentTypes =
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                "</Types>";
            const string rootRels =
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                "</Relationships>";

            var zip = new StoredZip();
            zip.Add("[Content_Types].xml", contentTypes);
            zip.Add("_rels/.rels", rootRels);
            zip.Add("xl/workbook.xml", workbook);
            zip.Add("xl/_rels/workbook.xml.rels", workbookRels);
            zip.Add("xl/styles.xml", styles);
            zip.Add("xl/worksheets/sheet1.xml", sheet.ToString());
            return zip.ToArray();
        }

        private static void AppendRow(StringBuilder sb, int rowNumber, IList<string> cells, bool header)
        {
            var r = rowNumber.ToString(CultureInfo.InvariantCulture);
            sb.Append("<row r=\"").Append(r).Append("\">");
            for (var c = 0; c < cells.Count; c++)
            {
                var value = cells[c] ?? string.Empty;
                var reference = ColumnName(c + 1) + r;
                double number;
                // Numbers stay numbers (so Excel can sum them); anything with a leading zero or non-numeric text is kept as text.
                if (!header && value.Length > 0 && value.Length < 16 && !value.StartsWith("0", StringComparison.Ordinal) &&
                    double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && !value.Contains("/") && !value.Contains("-"))
                {
                    sb.Append("<c r=\"").Append(reference).Append("\"><v>").Append(number.ToString("R", CultureInfo.InvariantCulture)).Append("</v></c>");
                    continue;
                }
                sb.Append("<c r=\"").Append(reference).Append("\" t=\"inlineStr\"").Append(header ? " s=\"1\"" : string.Empty)
                    .Append("><is><t xml:space=\"preserve\">").Append(Escape(Clip(value))).Append("</t></is></c>");
            }
            sb.Append("</row>");
        }

        private static string Clip(string value)
        {
            var cleaned = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                if (ch == '\t' || ch == '\n' || ch == '\r' || ch >= ' ') cleaned.Append(ch);
            }
            return cleaned.Length > 32000 ? cleaned.ToString(0, 32000) : cleaned.ToString();
        }

        private static string SafeSheetName(string name)
        {
            var cleaned = new StringBuilder();
            foreach (var ch in name ?? "Sheet1")
                cleaned.Append("[]:*?/\\".IndexOf(ch) >= 0 ? ' ' : ch);
            var result = cleaned.ToString().Trim();
            if (result.Length == 0) result = "Sheet1";
            return result.Length > 31 ? result.Substring(0, 31) : result;
        }

        public static string ColumnName(int index)
        {
            var name = string.Empty;
            while (index > 0)
            {
                var rem = (index - 1) % 26;
                name = (char)('A' + rem) + name;
                index = (index - 1) / 26;
            }
            return name;
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        /// <summary>ZIP container with stored entries (no compression) — enough for Excel and every unzip tool.</summary>
        private sealed class StoredZip
        {
            private static readonly uint[] CrcTable = BuildCrcTable();
            private readonly MemoryStream body = new MemoryStream();
            private readonly MemoryStream central = new MemoryStream();
            private int count;

            public void Add(string name, string xml)
            {
                var data = new UTF8Encoding(false).GetBytes(xml);
                var nameBytes = Encoding.UTF8.GetBytes(name);
                var crc = Crc32(data);
                var offset = (uint)body.Position;
                var time = DosTime(DateTime.Now);
                // Local file header
                Write(body, 0x04034b50u); Write(body, (ushort)20); Write(body, (ushort)0x0800); Write(body, (ushort)0);
                Write(body, (ushort)(time & 0xFFFF)); Write(body, (ushort)(time >> 16));
                Write(body, crc); Write(body, (uint)data.Length); Write(body, (uint)data.Length);
                Write(body, (ushort)nameBytes.Length); Write(body, (ushort)0);
                body.Write(nameBytes, 0, nameBytes.Length);
                body.Write(data, 0, data.Length);
                // Central directory entry
                Write(central, 0x02014b50u); Write(central, (ushort)20); Write(central, (ushort)20); Write(central, (ushort)0x0800); Write(central, (ushort)0);
                Write(central, (ushort)(time & 0xFFFF)); Write(central, (ushort)(time >> 16));
                Write(central, crc); Write(central, (uint)data.Length); Write(central, (uint)data.Length);
                Write(central, (ushort)nameBytes.Length); Write(central, (ushort)0); Write(central, (ushort)0);
                Write(central, (ushort)0); Write(central, (ushort)0); Write(central, 0u); Write(central, offset);
                central.Write(nameBytes, 0, nameBytes.Length);
                count++;
            }

            public byte[] ToArray()
            {
                var result = new MemoryStream();
                body.WriteTo(result);
                var centralOffset = (uint)result.Position;
                central.WriteTo(result);
                Write(result, 0x06054b50u); Write(result, (ushort)0); Write(result, (ushort)0);
                Write(result, (ushort)count); Write(result, (ushort)count);
                Write(result, (uint)central.Length); Write(result, centralOffset); Write(result, (ushort)0);
                return result.ToArray();
            }

            private static uint DosTime(DateTime t)
            {
                return (uint)(((t.Year - 1980) << 25) | (t.Month << 21) | (t.Day << 16) | (t.Hour << 11) | (t.Minute << 5) | (t.Second / 2));
            }

            private static void Write(Stream s, uint value)
            {
                s.WriteByte((byte)value); s.WriteByte((byte)(value >> 8)); s.WriteByte((byte)(value >> 16)); s.WriteByte((byte)(value >> 24));
            }

            private static void Write(Stream s, ushort value)
            {
                s.WriteByte((byte)value); s.WriteByte((byte)(value >> 8));
            }

            private static uint[] BuildCrcTable()
            {
                var table = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    var c = n;
                    for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    table[n] = c;
                }
                return table;
            }

            private static uint Crc32(byte[] data)
            {
                var c = 0xFFFFFFFFu;
                foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
                return c ^ 0xFFFFFFFFu;
            }
        }
    }
}
