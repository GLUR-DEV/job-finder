using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JobFinder.Web.Data;

/// <summary>
/// The one row of settings and state for the automatic SEEK watch (stored in watch.db).
/// The site owns the schedule; the Chrome extension only asks "is a scan due?" and does the scraping.
/// </summary>
public class WatchSettings
{
    /// <summary>Shortest time allowed between scans, so SEEK isn't hit too often.</summary>
    public const int MinIntervalMinutes = 120;
    public const int MaxIntervalMinutes = 7 * 24 * 60;

    [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; } = 1;

    // ---- Settings ----

    public bool Enabled { get; set; }
    public int IntervalMinutes { get; set; } = 240;

    /// <summary>The SEEK search results link to rescan. Set by the extension's scrape buttons.</summary>
    public string SearchUrl { get; set; } = "";
    public DateTime? SearchUrlUpdatedAt { get; set; }

    /// <summary>The random ntfy topic notifications are published to. Anyone who knows it can read them.</summary>
    public string Topic { get; set; } = "";

    // ---- Which new jobs are worth a notification (same filters as the Jobs page, plus exclude words) ----

    public string Keyword { get; set; } = "";
    public string Location { get; set; } = "";
    public string WorkType { get; set; } = "";
    public string Classification { get; set; } = "";
    public string SubClass { get; set; } = "";
    public int ListedDays { get; set; }

    /// <summary>"", "strong", "very" or "both".</summary>
    public string Applicant { get; set; } = "";

    /// <summary>Words that rule a job out, separated by commas or new lines. Checked against title, company and description.</summary>
    public string ExcludeKeywords { get; set; } = "";

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
        f.Load(Keyword, Location, WorkType, Classification, SubClass, ListedDays, Applicant, null, null);
        return f;
    }
}
