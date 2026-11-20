using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace JobRadar.Feeds.Text;

/// <summary>Small, pure text helpers shared by the board adapters.</summary>
public static partial class FeedText
{
    public const int DescriptionCap = 20_000;

    /// <summary>Entity-decoded, whitespace-collapsed and length-capped; empty for anything that is not text.</summary>
    public static string Clean(string? value, int max = 300)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var text = Whitespace().Replace(WebUtility.HtmlDecode(value), " ").Trim();
        return text.Length > max ? text[..max].TrimEnd() : text;
    }

    /// <summary>
    /// HTML (or entity-escaped HTML, which Greenhouse sends) to readable plain text: block tags become line breaks,
    /// list items become bullets, scripts and styles disappear.
    /// </summary>
    public static string HtmlToText(string? html, int cap = DescriptionCap)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return "";
        }

        var s = html;
        if (EscapedTag().IsMatch(s) && !RealTag().IsMatch(s))
        {
            s = WebUtility.HtmlDecode(s);
        }

        s = ScriptOrStyle().Replace(s, " ");
        s = LineBreak().Replace(s, "\n");
        s = ListItem().Replace(s, "\n• ");
        s = BlockEnd().Replace(s, "\n");
        s = AnyTag().Replace(s, " ");
        s = WebUtility.HtmlDecode(s);
        s = InlineSpace().Replace(s, " ");
        s = SpaceAroundNewline().Replace(s, "\n");
        s = ManyNewlines().Replace(s, "\n\n").Trim();
        return s.Length > cap ? string.Concat(s.AsSpan(0, cap).TrimEnd(), "…") : s;
    }

    /// <summary>Only absolute http(s) links make it into a posting.</summary>
    public static string? SafeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.Scheme is "https" or "http" ? uri.AbsoluteUri : null;
    }

    /// <summary>ISO-8601 strings (a naive one is UTC) or Unix seconds/milliseconds.</summary>
    public static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch) && value.Length is >= 9 and <= 13)
        {
            return epoch < 1_000_000_000_000
                ? DateTimeOffset.FromUnixTimeSeconds(epoch)
                : DateTimeOffset.FromUnixTimeMilliseconds(epoch);
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>"Germany, Austria; Remote / EMEA and UK" to its places, flags and one-letter noise removed.</summary>
    public static IReadOnlyList<string> SplitPlaces(string? value) =>
        PlaceSeparator().Split(Clean(value, 4000))
            .Select(p => p.Trim())
            .Where(p => p.Length > 1)
            .Take(80)
            .ToArray();

    /// <summary>A yearly USD figure as a whole number, or null for other currencies, zeros and junk.</summary>
    public static int? UsdPerYear(decimal? amount, string? currency, string? period = null)
    {
        if (amount is null || !string.Equals(currency ?? "USD", "USD", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!string.IsNullOrEmpty(period) && !YearlyPeriod().IsMatch(period))
        {
            return null;
        }

        return amount is >= 1_000 and < 2_000_000 ? (int)Math.Round(amount.Value) : null;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"&lt;/?[a-z]", RegexOptions.IgnoreCase)]
    private static partial Regex EscapedTag();

    [GeneratedRegex(@"</?[a-z][^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex RealTag();

    [GeneratedRegex(@"<(script|style)[\s\S]*?</\1>", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptOrStyle();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"<li[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ListItem();

    [GeneratedRegex(@"</(p|div|h[1-6]|li|ul|ol|tr|section)>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEnd();

    [GeneratedRegex(@"<[^>]+>|</?[a-z][^>]*$", RegexOptions.IgnoreCase)]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"[ \t ]+")]
    private static partial Regex InlineSpace();

    [GeneratedRegex(@" *\n *")]
    private static partial Regex SpaceAroundNewline();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ManyNewlines();

    [GeneratedRegex(@"\s*(?:,|;|\||/|\band\b)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex PlaceSeparator();

    [GeneratedRegex("year|annual", RegexOptions.IgnoreCase)]
    private static partial Regex YearlyPeriod();
}
