using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace BPlus.LiveMap.Core
{
    public static class XlsxWriter
    {
        // One sheet: Point, Easting, Northing, Elevation, Description. Numbers are real numbers, text is inline.
        public static void WritePoints(Stream s, IEnumerable<SurveyPoint> pts)
        {
            var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
            string[] h = { "Point", "Easting", "Northing", "Elevation", "Description" };
            sb.Append("<row r=\"1\">"); for (int i = 0; i < 5; i++) sb.Append(Str((char)('A' + i) + "1", h[i])); sb.Append("</row>");
            int r = 1;
            foreach (var p in pts)
            {
                r++; string n = r.ToString(CultureInfo.InvariantCulture);
                sb.Append("<row r=\"").Append(n).Append("\">");
                sb.Append(Str("A" + n, p.No)).Append(Num("B" + n, p.E)).Append(Num("C" + n, p.N));
                if (p.HasZ) sb.Append(Num("D" + n, p.Z));
                sb.Append(Str("E" + n, p.Desc)).Append("</row>");
            }
            sb.Append("</sheetData></worksheet>");
            using (var z = new ZipArchive(s, ZipArchiveMode.Create, true))
            {
                Put(z, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
                Put(z, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
                Put(z, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Points\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
                Put(z, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
                Put(z, "xl/worksheets/sheet1.xml", sb.ToString());
            }
        }
        static string Str(string r, string t) { return "<c r=\"" + r + "\" t=\"inlineStr\"><is><t xml:space=\"preserve\">" + SecurityElement.Escape(t ?? "") + "</t></is></c>"; }
        static string Num(string r, double v) { return "<c r=\"" + r + "\"><v>" + v.ToString("0.###############", CultureInfo.InvariantCulture) + "</v></c>"; }
        static void Put(ZipArchive z, string n, string c) { var e = z.CreateEntry(n); using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false))) w.Write(c); }
    }
}
