using System.Text.RegularExpressions;
using JobRadar.Contracts;

namespace JobRadar.Feeds.Text;

/// <summary>Seniority and work mode read from free text. The title wins over a feed's level field.</summary>
public static partial class Classify
{
    public static Seniority SeniorityOf(string title, string? levelHint = null)
    {
        var t = $" {title.ToLowerInvariant()} ";
        if (Intern().IsMatch(t))
        {
            return Seniority.Intern;
        }

        if (Lead().IsMatch(t))
        {
            return Seniority.Lead;
        }

        if (Senior().IsMatch(t))
        {
            return Seniority.Senior;
        }

        if (Junior().IsMatch(t) || LevelOne().IsMatch(t.TrimEnd()))
        {
            return Seniority.Junior;
        }

        if (Mid().IsMatch(t))
        {
            return Seniority.Mid;
        }

        var hint = (levelHint ?? "").ToLowerInvariant();
        return hint switch
        {
            _ when HintLead().IsMatch(hint) => Seniority.Lead,
            _ when hint.Contains("senior", StringComparison.Ordinal) => Seniority.Senior,
            _ when HintJunior().IsMatch(hint) => Seniority.Junior,
            _ when HintMid().IsMatch(hint) => Seniority.Mid,
            _ => Seniority.Unknown,
        };
    }

    public static WorkMode WorkModeOf(string? text)
    {
        var t = (text ?? "").ToLowerInvariant();
        if (Hybrid().IsMatch(t))
        {
            return WorkMode.Hybrid;
        }

        if (Remote().IsMatch(t))
        {
            return WorkMode.Remote;
        }

        return Onsite().IsMatch(t) ? WorkMode.Onsite : WorkMode.Unknown;
    }

    /// <summary>Feed vocabularies ("remote", "on-site", "hybrid") to the shared enum; null when the field says nothing.</summary>
    public static WorkMode? WorkModeField(string? value) => (value ?? "").Trim().ToLowerInvariant() switch
    {
        "remote" => WorkMode.Remote,
        "hybrid" => WorkMode.Hybrid,
        "onsite" or "on-site" or "on site" or "office" => WorkMode.Onsite,
        _ => null,
    };

    [GeneratedRegex(@"\b(intern|internship|praktikum|werkstudent|working student|trainee)\b")]
    private static partial Regex Intern();

    [GeneratedRegex(@"\b(staff|principal|lead|head|director|vp|chief|architect|distinguished|manager)\b")]
    private static partial Regex Lead();

    [GeneratedRegex(@"\b(senior|sr\.?|snr)(?=\W)|\b(iii|iv)\b")]
    private static partial Regex Senior();

    [GeneratedRegex(@"\b(junior|jr\.?|entry|graduate|associate)(?=\W)")]
    private static partial Regex Junior();

    [GeneratedRegex(@"\s(i|1)(?=\s*(?:,|-|–|\(|$))")]
    private static partial Regex LevelOne();

    [GeneratedRegex(@"\b(mid|intermediate|ii)\b")]
    private static partial Regex Mid();

    [GeneratedRegex("director|executive|principal|staff|lead|manager")]
    private static partial Regex HintLead();

    [GeneratedRegex("entry|junior|graduate|associate")]
    private static partial Regex HintJunior();

    [GeneratedRegex("mid|intermediate")]
    private static partial Regex HintMid();

    [GeneratedRegex(@"\bhybrid\b")]
    private static partial Regex Hybrid();

    [GeneratedRegex(@"\b(remote|anywhere|worldwide|work from home|homeoffice|wfh|telecommut)")]
    private static partial Regex Remote();

    [GeneratedRegex(@"\b(on-?site|in office|office-based)\b")]
    private static partial Regex Onsite();
}
