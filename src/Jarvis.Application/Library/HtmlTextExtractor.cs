using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Jarvis.Application.Library;

/// <summary>
/// Turns an HTML page into readable text without a parser: it drops scripts, styles and page furniture, prefers the
/// article or main region, keeps paragraph breaks, and decodes entities. Good enough for reading and summarising
/// articles, not a faithful renderer.
/// </summary>
public static partial class HtmlTextExtractor
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    public sealed record Extracted(string? Title, string Text);

    public static Extracted Extract(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return new Extracted(null, string.Empty);
        try
        {
            var title = TitleOf(html);
            var cleaned = Comments().Replace(html, " ");
            cleaned = Dropped().Replace(cleaned, " ");
            cleaned = Furniture().Replace(cleaned, " ");
            var region = Region("article", cleaned) ?? Region("main", cleaned) ?? Region("body", cleaned) ?? cleaned;
            region = ListItem().Replace(region, "\n• ");
            region = Block().Replace(region, "\n");
            region = Tag().Replace(region, " ");
            var text = WebUtility.HtmlDecode(region);
            return new Extracted(title, Tidy(text));
        }
        catch (RegexMatchTimeoutException)
        {
            return new Extracted(null, string.Empty);
        }
    }

    private static string? TitleOf(string html)
    {
        var meta = OgTitle().Match(html);
        var title = meta.Success ? meta.Groups[1].Value : null;
        title ??= TitleTag().Match(html) is { Success: true } t ? t.Groups[1].Value : null;
        title ??= H1().Match(html) is { Success: true } h ? Tag().Replace(h.Groups[1].Value, " ") : null;
        var clean = LibraryRules.Clean(WebUtility.HtmlDecode(title ?? string.Empty));
        return clean is null ? null : LibraryRules.Limit(clean, LibraryRules.MaxTitleLength);
    }

    private static string? Region(string tag, string html)
    {
        var match = Regex.Match(html, $@"<{tag}\b[^>]*>(.*?)</{tag}>", RegexOptions.Singleline | RegexOptions.IgnoreCase,
            Timeout);
        return match.Success && match.Groups[1].Value.Length > 400 ? match.Groups[1].Value : null;
    }

    private static string Tidy(string text)
    {
        var builder = new StringBuilder();
        var blank = false;
        foreach (var raw in text.Replace("\r", "\n").Split('\n'))
        {
            var line = string.Join(' ', raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (line.Length < 3)
            {
                blank = builder.Length > 0;
                continue;
            }
            if (blank) builder.Append('\n');
            builder.Append(line).Append('\n');
            blank = false;
        }
        return LibraryRules.CleanContent(builder.ToString());
    }

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline, matchTimeoutMilliseconds: 2000)]
    private static partial Regex Comments();

    [GeneratedRegex(@"<(script|style|noscript|svg|iframe|template|head)\b[^>]*>.*?</\1\s*>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 2000)]
    private static partial Regex Dropped();

    [GeneratedRegex(@"<(nav|footer|aside|form|header|figure|dialog)\b[^>]*>.*?</\1\s*>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 2000)]
    private static partial Regex Furniture();

    [GeneratedRegex(@"<li\b[^>]*>", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 2000)]
    private static partial Regex ListItem();

    [GeneratedRegex(@"</?(p|div|br|li|ul|ol|h[1-6]|tr|section|article|blockquote|pre|table|hr)\b[^>]*>",
        RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 2000)]
    private static partial Regex Block();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    private static partial Regex Tag();

    [GeneratedRegex("""<meta[^>]+property=["']og:title["'][^>]+content=["']([^"']+)["']""", RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex OgTitle();

    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex TitleTag();

    [GeneratedRegex(@"<h1\b[^>]*>(.*?)</h1>", RegexOptions.Singleline | RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex H1();
}
