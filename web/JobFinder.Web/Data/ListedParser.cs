using System.Text.RegularExpressions;

namespace JobFinder.Web.Data;

/// <summary>
/// Turns SEEK's relative listing text (for example "3d ago", "2h ago", "30+ d ago")
/// into an approximate date, so jobs can be filtered by how recently they were listed.
/// </summary>
public static partial class ListedParser
{
    [GeneratedRegex(@"(\d+)\s*\+?\s*(months?|mos?|minutes?|mins?|hours?|hrs?|days?|weeks?|wks?|m|h|d|w)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();

    public static DateTime? Parse(string? text, DateTime scrapedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim().ToLowerInvariant();

        if (t.Contains("yesterday")) return scrapedAtUtc.AddDays(-1);
        if (t.Contains("today") || t.Contains("just now") || t.Contains("just listed")) return scrapedAtUtc;

        var m = Pattern().Match(t);
        if (!m.Success || !int.TryParse(m.Groups[1].Value, out var n) || n > 3650) return null;

        var unit = m.Groups[2].Value;
        if (unit.StartsWith("mo")) return scrapedAtUtc.AddDays(-30.0 * n);
        if (unit.StartsWith("mi") || unit == "m") return scrapedAtUtc.AddMinutes(-n);
        if (unit.StartsWith('h')) return scrapedAtUtc.AddHours(-n);
        if (unit.StartsWith('d')) return scrapedAtUtc.AddDays(-n);
        if (unit.StartsWith('w')) return scrapedAtUtc.AddDays(-7.0 * n);
        return null;
    }
}