using System.IO.Compression;
using System.Text;
using Jarvis.Application.Files;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class OfficeTextTests
{
    private const string W = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"";
    private const string A = "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"";

    private static MemoryStream Zip(params (string Name, string Content)[] parts)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, content) in parts)
            {
                var entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream Docx(string bodyXml) =>
        Zip(("word/document.xml", $"<?xml version=\"1.0\"?><w:document {W}><w:body>{bodyXml}</w:body></w:document>"));

    [Fact]
    public void A_word_document_gives_its_paragraphs_runs_tabs_breaks_and_table_cells()
    {
        using var file = Docx("""
            <w:p><w:r><w:t>Quarterly </w:t></w:r><w:r><w:t xml:space="preserve">plan</w:t></w:r></w:p>
            <w:p><w:r><w:t>Owner</w:t></w:r><w:r><w:tab/></w:r><w:r><w:t>Sam</w:t></w:r></w:p>
            <w:p><w:r><w:t>Line one</w:t><w:br/><w:t>Line two</w:t></w:r></w:p>
            <w:tbl><w:tr><w:tc><w:p><w:r><w:t>Cost</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>€4,200</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
            <w:p><w:r><w:t>R&amp;D &lt;draft&gt; café</w:t></w:r></w:p>
            """);

        var text = OfficeText.Extract(file, OfficeText.DocxContentType, 10_000);

        Assert.Contains("Quarterly plan", text, StringComparison.Ordinal);
        Assert.Contains("Owner\tSam", text, StringComparison.Ordinal);
        Assert.Contains("Line one\nLine two", text, StringComparison.Ordinal);
        Assert.Contains("Cost", text, StringComparison.Ordinal);
        Assert.Contains("€4,200", text, StringComparison.Ordinal);
        Assert.Contains("R&D <draft> café", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Adjacent_text_runs_are_all_kept()
    {
        // The reader moves past an element when it reads its text; a naive loop then skips the next run.
        using var file = Docx("<w:p><w:r><w:t>a</w:t><w:t>b</w:t><w:t>c</w:t></w:r></w:p>");

        Assert.Equal("abc", OfficeText.Extract(file, OfficeText.DocxContentType, 1_000));
    }

    [Fact]
    public void A_presentation_is_read_slide_by_slide_in_numeric_order()
    {
        string Slide(string text) =>
            $"<p:sld xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" {A}><p:cSld><p:spTree><p:sp><p:txBody><a:p><a:r><a:t>{text}</a:t></a:r></a:p></p:txBody></p:sp></p:spTree></p:cSld></p:sld>";
        using var file = Zip(("ppt/slides/slide10.xml", Slide("Ten")), ("ppt/slides/slide2.xml", Slide("Two")),
            ("ppt/slides/slide1.xml", Slide("One")), ("ppt/notesSlides/notesSlide1.xml", Slide("Secret notes")));

        var text = OfficeText.Extract(file, OfficeText.PptxContentType, 10_000);

        Assert.True(text.IndexOf("One", StringComparison.Ordinal) < text.IndexOf("Two", StringComparison.Ordinal));
        Assert.True(text.IndexOf("Two", StringComparison.Ordinal) < text.IndexOf("Ten", StringComparison.Ordinal));
        Assert.Contains("Slide 10:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret notes", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Long_documents_stop_at_the_character_limit()
    {
        using var file = Docx(string.Concat(Enumerable.Repeat("<w:p><w:r><w:t>word word word</w:t></w:r></w:p>", 5_000)));

        var text = OfficeText.Extract(file, OfficeText.DocxContentType, 1_000);

        Assert.InRange(text.Length, 1_000, 1_100);
    }

    [Fact]
    public void A_part_that_unpacks_far_larger_than_the_limit_is_refused_as_a_zip_bomb()
    {
        var bomb = "<?xml version=\"1.0\"?><w:document " + W + "><w:body>" +
                   string.Concat(Enumerable.Repeat("<w:p><w:r><w:t></w:t></w:r></w:p>", 1_300_000)) + "</w:body></w:document>";
        Assert.True(bomb.Length > OfficeText.MaxPartBytes);
        using var file = Zip(("word/document.xml", bomb));
        Assert.True(file.Length < 2_000_000, "the archive itself is small");

        Assert.Throws<InvalidDataException>(() => OfficeText.Extract(file, OfficeText.DocxContentType, 10_000));
    }

    [Fact]
    public void A_dtd_or_external_entity_is_refused_and_never_resolved()
    {
        using var file = Zip(("word/document.xml", $$"""
            <?xml version="1.0"?><!DOCTYPE d [<!ENTITY secret SYSTEM "file:///etc/passwd">]>
            <w:document {{W}}><w:body><w:p><w:r><w:t>&secret;</w:t></w:r></w:p></w:body></w:document>
            """));

        var error = Assert.Throws<InvalidDataException>(() => OfficeText.Extract(file, OfficeText.DocxContentType, 10_000));

        Assert.DoesNotContain("root:", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Archives_with_too_many_parts_are_refused()
    {
        var parts = Enumerable.Range(0, OfficeText.MaxEntries + 1).Select(index => ($"junk/{index}.txt", "x")).ToList();
        parts.Add(("word/document.xml", $"<w:document {W}><w:body/></w:document>"));
        using var file = Zip([.. parts]);

        Assert.Throws<InvalidDataException>(() => OfficeText.Extract(file, OfficeText.DocxContentType, 1_000));
    }

    [Fact]
    public void Something_that_is_not_an_office_document_is_refused()
    {
        using var notZip = new MemoryStream("just some text"u8.ToArray());
        Assert.Throws<InvalidDataException>(() => OfficeText.Extract(notZip, OfficeText.DocxContentType, 1_000));

        using var noBody = Zip(("hello.txt", "hi"));
        Assert.Throws<InvalidDataException>(() => OfficeText.Extract(noBody, OfficeText.DocxContentType, 1_000));

        using var noSlides = Zip(("word/document.xml", $"<w:document {W}/>"));
        Assert.Throws<InvalidDataException>(() => OfficeText.Extract(noSlides, OfficeText.PptxContentType, 1_000));

        using var other = Docx("");
        Assert.Throws<InvalidDataException>(() => OfficeText.Extract(other, "application/zip", 1_000));
    }

    [Theory]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document", true)]
    [InlineData("application/vnd.openxmlformats-officedocument.presentationml.presentation", true)]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", false)]
    [InlineData("application/zip", false)]
    public void Only_word_and_powerpoint_types_count_as_office(string type, bool office) =>
        Assert.Equal(office, OfficeText.IsOffice(type));

    [Theory]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("application/yaml")]
    [InlineData("text/tab-separated-values")]
    [InlineData("text/calendar")]
    public void The_new_types_are_indexed(string type) => Assert.True(FileIndexing.IsIndexable(type));
}
