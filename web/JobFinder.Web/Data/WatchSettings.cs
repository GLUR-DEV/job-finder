using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JobFinder.Web.Data;

/// <summary>
/// Settings and state for one site's automatic watch (stored in watch.db). There is one row for SEEK and one for
/// Trade Me. The site owns the schedule; the Chrome extension only asks "is a scan due?" and does the scraping.
///
/// The on/off switch, interval and ntfy topic are shared by both sites and live on the SEEK row (Id 1). Everything
/// else, such as the search link, the filters and the state of the last scan, belongs to the row's own site.
/// </summary>
public class WatchSettings
{
    /// <summary>Shortest time allowed between scans, so SEEK isn't hit too often.</summary>
    public const int MinIntervalMinutes = 120;
    public const int MaxIntervalMinutes = 7 * 24 * 60;

    public const int SeekId = 1;
    public const int TradeMeId = 2;

    public static int IdFor(string source) => source == JobSources.TradeMe ? TradeMeId : SeekId;

    [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; } = SeekId;

    /// <summary>Which site this row is the watch for: <see cref="JobSources.Seek"/> or <see cref="JobSources.TradeMe"/>.</summary>
    public string Source { get; set; } = JobSources.Seek;

    // ---- Shared by both sites (only the SEEK row's values are used) ----

    public bool Enabled { get; set; }
    public int IntervalMinutes { get; set; } = 240;

    /// <summary>The random ntfy topic notifications are published to. Anyone who knows it can read them.</summary>
    public string Topic { get; set; } = "";

    // ---- This site's search ----

    /// <summary>The search results link to rescan. Set by the extension's scrape buttons.</summary>
    public string SearchUrl { get; set; } = "";
    public DateTime? SearchUrlUpdatedAt { get; set; }

    // ---- Which new jobs are worth a notification (same filters as the Jobs page, plus exclude words) ----

    public string Keyword { get; set; } = "";

    /// <summary>SEEK only: the suburb text SEEK gave the job, for example "Penrose, Auckland".</summary>
    public string Location { get; set; } = "";

    /// <summary>Trade Me only: the Trade Me district, for example "North Shore City".</summary>
    public string District { get; set; } = "";

    public string WorkType { get; set; } = "";
    public string Classification { get; set; } = "";
    public string SubClass { get; set; } = "";
    public int ListedDays { get; set; }

    /// <summary>SEEK only. "", "strong", "very" or "both".</summary>
    public string Applicant { get; set; } = "";

    /// <summary>Words that rule a job out, separated by commas or new lines. Checked against title, company and description.</summary>
    public string ExcludeKeywords { get; set; } = "";

    // ---- Trade Me only ----

    /// <summary>Leave out promoted listings.</summary>
    public bool SkipPromoted { get; set; }

    /// <summary>Only jobs that can be applied for with Quick apply.</summary>
    public bool QuickApplyOnly { get; set; }

    /// <summary>Only jobs where a pay figure was listed (on the card or in its teaser text).</summary>
    public bool PayListedOnly { get; set; }

    // ---- State ----

    /// <summary>When a scan was last handed to the extension. The next scan is due this long plus the interval after.</summary>
    public DateTime? LastAttemptAt { get; set; }

    /// <summary>Set by "Scan now". The extension picks it up on its next check.</summary>
    public bool RunRequested { get; set; }

    /// <summary>Id of the scan currently handed to the extension. Empty when none is running.</summary>
    public string ClaimId { get; set; } = "";
    public DateTime? ClaimedAt { get; set; }

    public DateTime? LastFinishedAt { get; set; }

    /// <summary>One sentence describing the last scan.</summary>
    public string LastResult { get; set; } = "";

    /// <summary>Empty when the last scan went fine.</summary>
    public string LastError { get; set; } = "";

    /// <summary>A copy that can be edited on the page without touching the stored row.</summary>
    public WatchSettings Copy() => (WatchSettings)MemberwiseClone();

    /// <summary>The Jobs-page filter these settings describe.</summary>
    public JobFilter ToFilter()
    {
        var f = new JobFilter();
        f.Load(Keyword, Location, WorkType, Classification, SubClass, ListedDays, Applicant, null, null, Source, District);
        return f;
    }
}
