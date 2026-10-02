using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Jarvis.Application.Reading;

/// <summary>
/// Pulls the title, site name, description and readable text out of an HTML page. It prefers the page's
/// &lt;article&gt; or &lt;main&gt; element and drops scripts, navigation, headers, footers and forms.
/// </summary>
public static partial class ReadingPageText
{
    public const int MaxTextLength = 200_000;

    public static FetchedPage FromHtml(string finalUrl, string html)
    {
        var title = Meta(html, "og:title") ?? Meta(html, "twitter:title") ?? TitleTag(html);
        var siteName = Meta(html, "og:site_name") ?? HostName(finalUrl);
        var description = Meta(html, "og:description") ?? Meta(html, "description");

        var cleaned = RemovedBlocks().Replace(Comments().Replace(html, " "), " ");
        var bodyMatch = BodyTag().Match(cleaned);
        var body = LargestMatch(Articles(), cleaned) ?? LargestMatch(Mains(), cleaned) ??
                   (bodyMatch.Success ? bodyMatch.Groups[1].Value : cleaned);
        var withoutChrome = ChromeBlocks().Replace(body, " ");
        return new FetchedPage(finalUrl, title, siteName, description, ToText(withoutChrome));
    }

    public static FetchedPage FromPlainText(string finalUrl, string text)
    {
        var clean = Collapse(text);
        var firstLine = clean.Split('\n', 2)[0].Trim();
        return new FetchedPage(finalUrl, firstLine.Length is > 0 and <= 160 ? firstLine : null, HostName(finalUrl),
            null, clean.Length > MaxTextLength ? clean[..MaxTextLength] : clean);
    }

    public static int CountWords(string? text) =>
        string.IsNullOrWhiteSpace(text) ? 0 : Words().Count(text);

    private static string ToText(string html)
    {
        var withBreaks = BlockBreaks().Replace(html, "\n");
        var stripped = Tags().Replace(withBreaks, " ");
        var decoded = WebUtility.HtmlDecode(stripped);
        var text = Collapse(decoded);
        return text.Length > MaxTextLength ? text[..MaxTextLength] : text;
    }

    private static string Collapse(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var line in text.Replace("\r", "").Split('\n'))
        {
            var clean = string.Join(' ', line.Split([' ', '\t', ' '], StringSplitOptions.RemoveEmptyEntries));
            if (clean.Length == 0) continue;
            if (builder.Length > 0) builder.Append('\n');
            builder.Append(clean);
        }
        return builder.ToString();
    }

    private static string? LargestMatch(Regex pattern, string html)
    {
        string? best = null;
        foreach (Match match in pattern.Matches(html))
        {
            var inner = match.Groups[1].Value;
            if (best is null || inner.Length > best.Length) best = inner;
        }
        return best is not null && CountWords(Tags().Replace(best, " ")) >= 40 ? best : null;
    }

    private static string? Meta(string html, string name)
    {
        foreach (Match tag in MetaTags().Matches(html))
        {
            var attributes = tag.Value;
            var key = Attribute(attributes, "property") ?? Attribute(attributes, "name");
            if (!string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) continue;
            var content = Attribute(attributes, "content");
            if (!string.IsNullOrWhiteSpace(content)) return Collapse(WebUtility.HtmlDecode(content)).Replace('\n', ' ');
        }
        return null;
    }

    private static string? Attribute(string tag, string name)
    {
        var match = Regex.Match(tag, $"""\b{name}\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+))""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!match.Success) return null;
        return match.Groups[1].Success ? match.Groups[1].Value :
            match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
    }

    private static string? TitleTag(string html)
    {
        var match = TitleElement().Match(html);
        if (!match.Success) return null;
        var title = Collapse(WebUtility.HtmlDecode(Tags().Replace(match.Groups[1].Value, " "))).Replace('\n', ' ');
        return title.Length == 0 ? null : title;
    }

    private static string? HostName(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host
            : null;

    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.Singleline |
                                         RegexOptions.CultureInvariant;

    [GeneratedRegex("<!--.*?-->", Options, 2000)]
    private static partial Regex Comments();

    [GeneratedRegex(@"<(script|style|noscript|svg|template|iframe|canvas|object)\b[^>]*>.*?</\1\s*>", Options, 2000)]
    private static partial Regex RemovedBlocks();

    [GeneratedRegex(@"<(nav|header|footer|aside|form|button|select|dialog|figure)\b[^>]*>.*?</\1\s*>", Options, 2000)]
    private static partial Regex ChromeBlocks();

    [GeneratedRegex(@"<article\b[^>]*>(.*?)</article\s*>", Options, 2000)]
    private static partial Regex Articles();

    [GeneratedRegex(@"<main\b[^>]*>(.*?)</main\s*>", Options, 2000)]
    private static partial Regex Mains();

    [GeneratedRegex(@"<body\b[^>]*>(.*?)(?:</body\s*>|$)", Options, 2000)]
    private static partial Regex BodyTag();

    [GeneratedRegex(@"<title\b[^>]*>(.*?)</title\s*>", Options, 2000)]
    private static partial Regex TitleElement();

    [GeneratedRegex(@"<meta\b[^>]*>", Options, 2000)]
    private static partial Regex MetaTags();

    [GeneratedRegex(@"<(?:br|/?p|/?div|/?h[1-6]|/?li|/?ul|/?ol|/?tr|/?blockquote|/?pre|/?section|hr)\b[^>]*>", Options, 2000)]
    private static partial Regex BlockBreaks();

    [GeneratedRegex(@"<[^>]+>", Options, 2000)]
    private static partial Regex Tags();

    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}'’\-]*", RegexOptions.CultureInvariant, 2000)]
    private static partial Regex Words();
}
