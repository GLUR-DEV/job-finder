using System.IO.Compression;
using System.Security;
using System.Text;
using System.Xml;

namespace JobFinder.Web.Data;

/// <summary>
/// Reads the text out of a .docx and writes plain text to a simple .docx, using only the built-in zip and XML classes
/// (a .docx is a zip of XML files), so no extra packages are needed.
/// </summary>
public static class WordDocs
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>The text of a .docx, one paragraph per line. Tables come out one cell per line.</summary>
    public static string ReadText(Stream docx)
    {
        using var zip = new ZipArchive(docx, ZipArchiveMode.Read);
        var entry = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException("That file isn't a Word document.");

        var xml = new XmlDocument();
        using (var s = entry.Open()) xml.Load(s);

        var ns = new XmlNamespaceManager(xml.NameTable);
        ns.AddNamespace("w", W);

        var sb = new StringBuilder();
        foreach (XmlNode p in xml.SelectNodes("//w:body//w:p", ns)!)
        {
            foreach (XmlNode n in p.SelectNodes(".//w:t | .//w:tab | .//w:br | .//w:cr", ns)!)
            {
                switch (n.LocalName)
                {
                    case "t": sb.Append(n.InnerText); break;
                    case "tab": sb.Append('\t'); break;
                    default: sb.Append('\n'); break;
                }
            }
            sb.Append('\n');
        }
        return Tidy(sb.ToString());
    }

    /// <summary>A .docx with one paragraph per line of the text, in 11 pt Calibri. Blank lines stay as blank paragraphs.</summary>
    public static byte[] Write(string text)
    {
        var body = new StringBuilder();
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            body.Append("<w:p><w:pPr><w:spacing w:after=\"0\" w:line=\"276\" w:lineRule=\"auto\"/></w:pPr>");
            if (line != "")
                body.Append("<w:r><w:t xml:space=\"preserve\">").Append(SecurityElement.Escape(line)).Append("</w:t></w:r>");
            body.Append("</w:p>");
        }

        var document =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            $"<w:document xmlns:w=\"{W}\"><w:body>{body}" +
            "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/>" +   // A4
            "<w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"708\" w:footer=\"708\" w:gutter=\"0\"/></w:sectPr>" +
            "</w:body></w:document>";

        var styles =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            $"<w:styles xmlns:w=\"{W}\"><w:docDefaults><w:rPrDefault><w:rPr>" +
            "<w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\" w:eastAsia=\"Calibri\" w:cs=\"Calibri\"/>" +
            "<w:sz w:val=\"22\"/><w:szCs w:val=\"22\"/><w:lang w:val=\"en-NZ\"/>" +
            "</w:rPr></w:rPrDefault></w:docDefaults></w:styles>";

        const string contentTypes =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
            "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>" +
            "</Types>";

        const string rels =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
            "</Relationships>";

        const string docRels =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
            "</Relationships>";

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", contentTypes);
            Add(zip, "_rels/.rels", rels);
            Add(zip, "word/document.xml", document);
            Add(zip, "word/styles.xml", styles);
            Add(zip, "word/_rels/document.xml.rels", docRels);
        }
        return ms.ToArray();
    }

    /// <summary>Trims each line and collapses runs of blank lines to one.</summary>
    public static string Tidy(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => l.TrimEnd());
        var sb = new StringBuilder();
        var blank = false;
        foreach (var l in lines)
        {
            if (l.Trim() == "") { blank = true; continue; }
            if (blank && sb.Length > 0) sb.Append('\n');
            sb.Append(l).Append('\n');
            blank = false;
        }
        return sb.ToString().Trim();
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        var e = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
        w.Write(content);
    }
}
