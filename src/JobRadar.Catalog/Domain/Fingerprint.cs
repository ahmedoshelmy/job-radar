using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using JobRadar.Contracts;

namespace JobRadar.Catalog.Domain;

/// <summary>
/// One opening, one row. The fingerprint is <c>norm(company)|norm(title)|place</c>: the same job mirrored on a company
/// board and an aggregator meets itself even though the URL, the title spelling and the company suffix differ.
/// A canonical URL (tracking parameters removed) is the second key. See ADR 0004.
/// </summary>
public static partial class Fingerprint
{
    public static string For(string company, string title, WorkMode workMode, string location)
    {
        var key = $"{NormCompany(company)}|{NormTitle(title)}|{PlaceBucket(workMode, location)}";
        return key.Length > 300 ? key[..300] : key;
    }

    /// <summary>"Nilewave Labs Inc." and "nilewave" are the same employer; legal and generic suffixes go.</summary>
    public static string NormCompany(string company)
    {
        var s = Fold(company).Replace("&", " and ", StringComparison.Ordinal).Replace(".", "", StringComparison.Ordinal).Replace(",", " ", StringComparison.Ordinal);
        s = CompanySuffix().Replace(s, " ");
        return Collapse(NonAlphaNum().Replace(s, " "));
    }

    /// <summary>
    /// "Sr. Back-end Engineer (.NET) - Remote" and "Senior Backend Engineer (.NET)" normalise alike: gender tags,
    /// remote markers and spelling variants go; abbreviations expand.
    /// </summary>
    public static string NormTitle(string title)
    {
        var s = Fold(title);
        s = GenderTag().Replace(s, " ");
        s = Senior().Replace(s, "senior");
        s = Junior().Replace(s, "junior");
        s = BackEnd().Replace(s, "backend");
        s = FrontEnd().Replace(s, "frontend");
        s = FullStack().Replace(s, "fullstack");
        s = DotNet().Replace(s, ".net");
        s = BracketedWhere().Replace(s, " ");
        s = TrailingWhere().Replace(s, " ");
        s = RemoteWord().Replace(s, " ");
        s = TitleNoise().Replace(s, " ");
        s = LoneDot().Replace(s, " ");
        return Collapse(s);
    }

    /// <summary>Remote jobs share one bucket; others are keyed by their first place (the city).</summary>
    public static string PlaceBucket(WorkMode workMode, string location)
    {
        if (workMode == WorkMode.Remote)
        {
            return "remote";
        }

        var first = Fold(location).Split([',', ';', '|', '/'])[0];
        first = Collapse(NonLetter().Replace(OfficeWord().Replace(first, ""), " "));
        return first.Length > 0 ? first : "any";
    }

    /// <summary>Host lowercased, no www, no fragment, no tracking parameters, no trailing slash.</summary>
    public static string CanonicalUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        var kept = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !TrackingParam().IsMatch(p.Split('=')[0]))
            .ToArray();
        var host = uri.Host.ToLowerInvariant();
        host = host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
        var path = uri.AbsolutePath.Length > 1 ? uri.AbsolutePath.TrimEnd('/') : "";
        var port = uri.IsDefaultPort ? "" : $":{uri.Port}";
        return $"{uri.Scheme}://{host}{port}{path}{(kept.Length > 0 ? "?" + string.Join('&', kept) : "")}";
    }

    private static string Fold(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
        {
            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    private static string Collapse(string s) => Spaces().Replace(s, " ").Trim();

    [GeneratedRegex(@"\b(inc|llc|ltd|limited|gmbh|ag|sa|sae|bv|plc|co|corp|corporation|company|group|holding|holdings|technologies|technology|labs)\b")]
    private static partial Regex CompanySuffix();

    [GeneratedRegex(@"[^a-z0-9؀-ۿ]+")]
    private static partial Regex NonAlphaNum();

    [GeneratedRegex(@"\((?:[mwfdx]\s*/\s*){1,3}[mwfdx]\)|\b(?:[mwfdx]/){2}[mwfdx]\b")]
    private static partial Regex GenderTag();

    [GeneratedRegex(@"\bsr\.?(?=\s|$)")]
    private static partial Regex Senior();

    [GeneratedRegex(@"\bjr\.?(?=\s|$)")]
    private static partial Regex Junior();

    [GeneratedRegex(@"\bback[- ]?end\b")]
    private static partial Regex BackEnd();

    [GeneratedRegex(@"\bfront[- ]?end\b")]
    private static partial Regex FrontEnd();

    [GeneratedRegex(@"\bfull[- ]?stack\b")]
    private static partial Regex FullStack();

    [GeneratedRegex(@"\bdot ?net\b")]
    private static partial Regex DotNet();

    [GeneratedRegex(@"[(\[][^)\]]*(remote|anywhere|worldwide|hybrid|on-?site)[^)\]]*[)\]]")]
    private static partial Regex BracketedWhere();

    [GeneratedRegex(@"\s[-–—|]\s*(remote|anywhere|worldwide)\b.*$")]
    private static partial Regex TrailingWhere();

    [GeneratedRegex(@"\bremote\b")]
    private static partial Regex RemoteWord();

    [GeneratedRegex(@"[^a-z0-9.#+؀-ۿ]+")]
    private static partial Regex TitleNoise();

    [GeneratedRegex(@"(?<=^|\s)\.(?=\s|$)")]
    private static partial Regex LoneDot();

    [GeneratedRegex(@"\b(office|hq)\b")]
    private static partial Regex OfficeWord();

    [GeneratedRegex(@"[^a-z؀-ۿ]+")]
    private static partial Regex NonLetter();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"^(utm_.*|ref|source|src|gh_src|lever-source.*|trk|refid|trackingid|fbclid|gclid)$", RegexOptions.IgnoreCase)]
    private static partial Regex TrackingParam();
}
