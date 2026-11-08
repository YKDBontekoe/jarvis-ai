using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Jarvis.Application.Files;

/// <summary>
/// Reads the text of Word (.docx) and PowerPoint (.pptx) files, which are zip archives of XML. Owners could not ask
/// about their Word documents at all before. Uploaded files are untrusted: only the few parts that hold text are
/// opened, a part larger than <see cref="MaxPartBytes"/> once unpacked is refused (a zip bomb), the archive may hold
/// at most <see cref="MaxEntries"/> entries, and XML is read without DTDs or external entities.
/// </summary>
public static partial class OfficeText
{
    public const string DocxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    public const string PptxContentType = "application/vnd.openxmlformats-officedocument.presentationml.presentation";

    public const long MaxPartBytes = 32 * 1024 * 1024;
    public const int MaxEntries = 5_000;

    public static bool IsOffice(string contentType) => contentType is DocxContentType or PptxContentType;

    /// <summary>The text of the document, paragraphs on their own lines. Throws <see cref="InvalidDataException"/> when the file is not a valid, safe archive.</summary>
    public static string Extract(Stream content, string contentType, int maxCharacters)
    {
        try
        {
            using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count > MaxEntries)
                throw new InvalidDataException("The document holds too many parts to read safely.");
            return contentType switch
            {
                DocxContentType => ReadDocx(archive, maxCharacters),
                PptxContentType => ReadPptx(archive, maxCharacters),
                _ => throw new InvalidDataException("Not an Office document type."),
            };
        }
        catch (XmlException exception)
        {
            // A DTD, an entity or broken XML: refused the same way as any other unreadable document.
            throw new InvalidDataException("The document's text is not valid XML.", exception);
        }
    }

    private static string ReadDocx(ZipArchive archive, int maxCharacters)
    {
        var body = archive.GetEntry("word/document.xml") ?? throw new InvalidDataException("This is not a Word document.");
        var text = new StringBuilder();
        // Word keeps header, footer and note text in separate parts; the body is what people ask about.
        ReadParts([body], text, maxCharacters, "w", "t", "p", tab: "tab", lineBreak: "br", cellEnd: "tc");
        return Tidy(text.ToString());
    }

    private static string ReadPptx(ZipArchive archive, int maxCharacters)
    {
        var slides = archive.Entries
            .Select(entry => (Entry: entry, Number: SlideNumber(entry.FullName)))
            .Where(item => item.Number is not null)
            .OrderBy(item => item.Number)
            .ToList();
        if (slides.Count == 0) throw new InvalidDataException("This is not a PowerPoint presentation.");
        var text = new StringBuilder();
        foreach (var (entry, number) in slides)
        {
            if (text.Length >= maxCharacters) break;
            text.Append("Slide ").Append(number).Append(":\n");
            ReadParts([entry], text, maxCharacters, "a", "t", "p", tab: null, lineBreak: "br", cellEnd: null);
            text.Append('\n');
        }
        return Tidy(text.ToString());
    }

    private static void ReadParts(IEnumerable<ZipArchiveEntry> parts, StringBuilder text, int maxCharacters,
        string prefix, string textElement, string paragraphElement, string? tab, string? lineBreak, string? cellEnd)
    {
        foreach (var part in parts)
        {
            if (part.Length > MaxPartBytes)
                throw new InvalidDataException("A part of the document is too large to read safely.");
            using var raw = part.Open();
            using var limited = new LimitedStream(raw, MaxPartBytes);
            using var reader = XmlReader.Create(limited, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                MaxCharactersFromEntities = 0,
            });
            var more = reader.Read();
            while (more && text.Length < maxCharacters)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.Prefix == prefix)
                {
                    if (reader.LocalName == textElement && !reader.IsEmptyElement)
                    {
                        // Reading the content also moves past the end tag: look at the node that follows as it is.
                        text.Append(reader.ReadElementContentAsString());
                        continue;
                    }
                    if (reader.LocalName == tab) text.Append('\t');
                    else if (reader.LocalName == lineBreak) text.Append('\n');
                }
                else if (reader.NodeType == XmlNodeType.EndElement && reader.Prefix == prefix)
                {
                    if (reader.LocalName == paragraphElement) text.Append('\n');
                    else if (reader.LocalName == cellEnd) text.Append('\t');
                }
                more = reader.Read();
            }
        }
    }

    private static int? SlideNumber(string path)
    {
        var match = SlidePath().Match(path);
        return match.Success && int.TryParse(match.Groups[1].Value, out var number) ? number : null;
    }

    private static string Tidy(string text) =>
        BlankRuns().Replace(text.Replace("\r", ""), "\n\n").Trim();

    [GeneratedRegex(@"^ppt/slides/slide(\d{1,5})\.xml$", RegexOptions.CultureInvariant)]
    private static partial Regex SlidePath();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankRuns();

    /// <summary>Stops reading after a fixed number of bytes: the declared size of a zip entry can lie.</summary>
    private sealed class LimitedStream(Stream inner, long limit) : Stream
    {
        private long _read;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, (int)Math.Min(count, limit - _read + 1));
            _read += read;
            if (_read > limit) throw new InvalidDataException("A part of the document is too large to read safely.");
            return read;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
