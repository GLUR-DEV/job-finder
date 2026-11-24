using System.Text.RegularExpressions;

namespace JobFinder.Web.Data;

/// <summary>
/// Looks through a job ad for listed work hours. It counts:
/// clock times and rosters (9am-5pm, 8.30-5), hours per week or day (27 hours per week, 20-30 hrs/week, 8-hour shifts),
/// full-time equivalent (0.8 FTE), and shift names (night shift, weekend shifts, rostered).
/// It is a text match, so odd wording can be missed and the odd false hit is possible.
/// </summary>
public static partial class HoursDetector
{
    // 27 hours per week, 20-30 hrs/week, 40 hours a week, 8-hour shifts, 10 hour days, 30 hours weekly
    [GeneratedRegex(@"(?<![\d.$])\d{1,2}(?:\.\d+)?(?:\s*(?:-|–|to)\s*\d{1,2}(?:\.\d+)?)?[\s+-]*(?:hours?|hrs?)\b(?:\s*(?:per|a|each|p\.?|/)\s*(?:week|wk|day|fortnight|month)\b|\s*(?:weekly|daily|fortnightly)\b|[\s-]*(?:shifts?|days?)\b)", RegexOptions.IgnoreCase)]
    private static partial Regex HoursPer();

    // 9am, 5.30pm, 9:00 am
    [GeneratedRegex(@"(?<![\d.$])(?:1[0-2]|0?[1-9])(?:[:.][0-5]\d)?\s*(?:am|pm|a\.m\.|p\.m\.)(?![a-z])", RegexOptions.IgnoreCase)]
    private static partial Regex ClockAmPm();

    // 8.30-5, 8:30 to 17:00, 9.00-5.00 (minutes must be :00, :15, :30 or :45 so prices like 27.80-28.80 don't match)
    [GeneratedRegex(@"(?<![\d.$])(?:[01]?\d|2[0-4])[:.](?:00|15|30|45)\s*(?:-|–|to|until)\s*(?:[01]?\d|2[0-4])(?:[:.](?:00|15|30|45))?(?![\d])", RegexOptions.IgnoreCase)]
    private static partial Regex ClockRange();

    // 9-5, 8 to 4 (not 9-5 years, 8-5 days, ...)
    [GeneratedRegex(@"(?<![\d.$])[7-9]\s*(?:-|–|to)\s*[4-6](?![\d.]|\s*(?:years?|yrs?|days?|weeks?|months?|hours?|hrs?|people|staff|%))", RegexOptions.IgnoreCase)]
    private static partial Regex NineToFive();

    // 0.8 FTE, FTE, full-time equivalent
    [GeneratedRegex(@"(?:\b\d(?:\.\d+)?\s*)?\bFTE\b|\bfull[- ]time equivalent\b", RegexOptions.IgnoreCase)]
    private static partial Regex Fte();

    // night shift, weekend shifts, rotating roster, shift work, rostered
    [GeneratedRegex(@"\b(?:day|night|evening|morning|afternoon|weekend|weekday|split|rotating|rotational|overnight|early|late|twilight|swing|graveyard)\s+(?:shifts?|rosters?)\b|\bshift\s+(?:work|patterns?|times?|hours)\b|\brostered\b|\brosters?\b", RegexOptions.IgnoreCase)]
    private static partial Regex ShiftNames();

    private static readonly Regex[] Patterns =
    {
        HoursPer(), ClockAmPm(), ClockRange(), NineToFive(), Fte(), ShiftNames()
    };

    public static bool Has(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (var p in Patterns)
            if (p.IsMatch(text)) return true;
        return false;
    }

    /// <summary>The first few distinct bits of text that were taken as listed hours, so you can see why a job was flagged.</summary>
    public static IReadOnlyList<string> Matches(string? text, int max = 8)
    {
        var found = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return found;

        foreach (var p in Patterns)
        {
            foreach (Match m in p.Matches(text))
            {
                var v = m.Value.Trim();
                if (v.Length > 0 && !found.Contains(v, StringComparer.OrdinalIgnoreCase))
                    found.Add(v);
                if (found.Count >= max) return found;
            }
        }
        return found;
    }
}
