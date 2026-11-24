using System.ComponentModel.DataAnnotations;

namespace JobFinder.Web.Data;

/// <summary>One scraped SEEK job. Mirrors every field the extension collects.</summary>
public class Job
{
    /// <summary>SEEK job id (or the job URL when no id could be found).</summary>
    [Key]
    public string Id { get; set; } = "";

    public string Title { get; set; } = "";
    public string Company { get; set; } = "";
    public string Location { get; set; } = "";
    public string Salary { get; set; } = "";
    public string WorkType { get; set; } = "";
    public string Classification { get; set; } = "";
    public string SubClass { get; set; } = "";

    /// <summary>The listed text exactly as SEEK showed it, for example "3d ago".</summary>
    public string Listed { get; set; } = "";

    /// <summary>Approximate listing date worked out from <see cref="Listed"/> and <see cref="ScrapedAt"/>. Null when the text could not be understood.</summary>
    public DateTime? ListedDate { get; set; }

    /// <summary>"Strong applicant", "Very strong applicant", or empty. See <see cref="ApplicantStrengths"/>.</summary>
    public string ApplicantStrength { get; set; } = "";

    /// <summary>Every badge shown on the card, separated by "; " (for example "Strong applicant; New to you").</summary>
    public string Badges { get; set; } = "";

    /// <summary>Extra listing info shown next to the date, such as "Viewed" or "Expiring".</summary>
    public string ListingStatus { get; set; } = "";

    /// <summary>SEEK's ad type for the card (for example ORGANIC).</summary>
    public string AdType { get; set; } = "";

    /// <summary>SEEK's results section for the card (for example MAIN).</summary>
    public string Section { get; set; } = "";

    public string Description { get; set; } = "";
    public string Url { get; set; } = "";

    /// <summary>When the extension scraped the job (UTC).</summary>
    public DateTime ScrapedAt { get; set; }

    /// <summary>When this site last received the job from the extension (UTC).</summary>
    public DateTime ReceivedAt { get; set; }
}
