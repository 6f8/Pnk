using System.IO.Compression;
using System.Security;
using System.Text;

namespace Kashif;

/// <summary>تصدير الجداول إلى ملف Excel (xlsx) بدون أي مكتبة خارجية</summary>
public static class Excel
{
    public static void ExportGrid(DataGridView grid, string title)
    {
        var cols = grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible && c.Name != "id").OrderBy(c => c.DisplayIndex).ToList();
        if (cols.Count == 0 || grid.Rows.Count == 0) { Ui.Warn("لا توجد بيانات للتصدير."); return; }
        using var sfd = new SaveFileDialog
        {
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = Safe(title) + "_" + DateTime.Now.ToString("yyyy-MM-dd") + ".xlsx"
        };
        if (sfd.ShowDialog() != DialogResult.OK) return;
        var rows = new List<object[]>();
        foreach (DataGridViewRow r in grid.Rows) if (r.Visible) rows.Add(cols.Select(c => r.Cells[c.Index].Value).ToArray());
        try
        {
            Write(sfd.FileName, title, cols.Select(c => c.HeaderText).ToArray(), rows);
            if (Ui.Confirm("تم التصدير بنجاح. هل تريد فتح الملف؟"))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
        }
        catch (Exception ex) { Ui.Warn("تعذر التصدير: " + ex.Message); }
    }

    static string Safe(string s) => string.Concat((s ?? "export").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    public static void Write(string path, string title, string[] headers, List<object[]> rows)
    {
        if (File.Exists(path)) File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Add(string name, string content)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
            w.Write(content);
        }

        Add("[Content_Types].xml", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types"">
<Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml""/>
<Default Extension=""xml"" ContentType=""application/xml""/>
<Override PartName=""/xl/workbook.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml""/>
<Override PartName=""/xl/worksheets/sheet1.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/>
<Override PartName=""/xl/styles.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml""/>
</Types>");
        Add("_rels/.rels", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
<Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""xl/workbook.xml""/>
</Relationships>");
        Add("xl/_rels/workbook.xml.rels", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
<Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet1.xml""/>
<Relationship Id=""rId2"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"" Target=""styles.xml""/>
</Relationships>");
        var sheetName = SecurityElement.Escape(new string(Safe(title).Where(c => c != '[' && c != ']' && c != '*' && c != '?' && c != ':').Take(30).ToArray()));
        Add("xl/workbook.xml", $@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<workbook xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships"">
<sheets><sheet name=""{(sheetName == "" ? "Sheet1" : sheetName)}"" sheetId=""1"" r:id=""rId1""/></sheets></workbook>");
        Add("xl/styles.xml", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<styleSheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"">
<numFmts count=""1""><numFmt numFmtId=""164"" formatCode=""#,##0.##""/></numFmts>
<fonts count=""2""><font><sz val=""11""/><name val=""Calibri""/></font><font><b/><sz val=""11""/><color rgb=""FFFFFFFF""/><name val=""Calibri""/></font></fonts>
<fills count=""3""><fill><patternFill patternType=""none""/></fill><fill><patternFill patternType=""gray125""/></fill>
<fill><patternFill patternType=""solid""><fgColor rgb=""FF0F2847""/><bgColor indexed=""64""/></patternFill></fill></fills>
<borders count=""1""><border><left/><right/><top/><bottom/><diagonal/></border></borders>
<cellStyleXfs count=""1""><xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0""/></cellStyleXfs>
<cellXfs count=""3""><xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0"" xfId=""0""/>
<xf numFmtId=""0"" fontId=""1"" fillId=""2"" borderId=""0"" xfId=""0"" applyFont=""1"" applyFill=""1""/>
<xf numFmtId=""164"" fontId=""0"" fillId=""0"" borderId=""0"" xfId=""0"" applyNumberFormat=""1""/></cellXfs>
</styleSheet>");

        var sb = new StringBuilder();
        sb.Append(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<worksheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"">
<sheetViews><sheetView rightToLeft=""1"" workbookViewId=""0""><pane ySplit=""1"" topLeftCell=""A2"" activePane=""bottomLeft"" state=""frozen""/></sheetView></sheetViews>
<cols>");
        for (int i = 0; i < headers.Length; i++) sb.Append($@"<col min=""{i + 1}"" max=""{i + 1}"" width=""18"" customWidth=""1""/>");
        sb.Append("</cols><sheetData>");
        sb.Append(@"<row r=""1"">");
        for (int i = 0; i < headers.Length; i++) sb.Append(Cell(i, 1, headers[i], 1));
        sb.Append("</row>");
        int rn = 2;
        foreach (var row in rows)
        {
            sb.Append($@"<row r=""{rn}"">");
            for (int i = 0; i < row.Length; i++) sb.Append(Cell(i, rn, row[i], 0));
            sb.Append("</row>");
            rn++;
        }
        sb.Append("</sheetData></worksheet>");
        Add("xl/worksheets/sheet1.xml", sb.ToString());
    }

    static string ColName(int i)
    {
        var s = "";
        i++;
        while (i > 0) { int m = (i - 1) % 26; s = (char)('A' + m) + s; i = (i - 1) / 26; }
        return s;
    }

    static string Cell(int col, int row, object v, int style)
    {
        var r = ColName(col) + row;
        switch (v)
        {
            case null: case DBNull: return "";
            case double or float or decimal or int or long:
                var num = Convert.ToDouble(v).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                return $@"<c r=""{r}"" s=""{(style == 0 ? 2 : style)}""><v>{num}</v></c>";
            default:
                var text = SecurityElement.Escape(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)) ?? "";
                return $@"<c r=""{r}"" t=""inlineStr"" s=""{style}""><is><t xml:space=""preserve"">{text}</t></is></c>";
        }
    }
}
