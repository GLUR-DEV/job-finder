using System.ComponentModel.DataAnnotations;

namespace JobFinder.Web.Data;

/// <summary>Everything scraped from a job's own SEEK page. One row per job, linked to <see cref="Job"/>.</summary>
public class JobDetail
{
    [Key]
    public string JobId { get; set; } = "";
    public Job? Job { get; set; }

    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public string Advertiser { get; set; } = "";
    public string AdvertiserUrl { get; set; } = "";
    public string Location { get; set; } = "";
    public string Classification { get; set; } = "";
    public string WorkType { get; set; } = "";

    /// <summary>Salary line from the page. SEEK sometimes puts benefits here instead.</summary>
    public string Salary { get; set; } = "";

    /// <summary>For example "Posted 6h ago".</summary>
    public string Posted { get; set; } = "";

    /// <summary>For example "Medium application volume".</summary>
    public string ApplicationVolume { get; set; } = "";

    /// <summary>Text of the apply button, for example "Quick apply" or "Apply".</summary>
    public string ApplyType { get; set; } = "";
    public string ApplyUrl { get; set; } = "";

    /// <summary>Badges at the top of the page (Strong applicant, Responsive hirer, ...), separated by "; ".</summary>
    public string Badges { get; set; } = "";

    public string SkillsSummary { get; set; } = "";

    /// <summary>Visible skills that match your SEEK profile, separated by "; ".</summary>
    public string SkillsMatched { get; set; } = "";

    /// <summary>Visible skills SEEK suggests adding to your profile, separated by "; ".</summary>
    public string SkillsSuggested { get; set; } = "";

    /// <summary>Questions the employer adds to the application, one per line.</summary>
    public string EmployerQuestions { get; set; } = "";

    /// <summary>Contact emails found in the ad, separated by "; ".</summary>
    public string Emails { get; set; } = "";

    /// <summary>Contact phone numbers found in the ad, separated by "; ".</summary>
    public string Phones { get; set; } = "";

    /// <summary>The full job ad as plain text.</summary>
    public string Description { get; set; } = "";

    public DateTime ScrapedAt { get; set; }
    public DateTime ReceivedAt { get; set; }
}
