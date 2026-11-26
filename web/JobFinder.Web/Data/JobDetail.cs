using System.ComponentModel.DataAnnotations;

namespace JobFinder.Web.Data;

/// <summary>
/// Everything scraped from a job's own page (SEEK or Trade Me). One row per job, linked to <see cref="Job"/>.
/// Trade Me pages fill the same fields where they can (advertiser, location, work type, pay as <see cref="Salary"/>,
/// the listed text as <see cref="Posted"/>, contacts and the ad), so the Favourites filters work on both sites.
/// Fields only one site has stay empty for the other.
/// </summary>
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

    // ---- Trade Me only (empty for SEEK jobs) ----

    /// <summary>For example "Permanent" or "Contract".</summary>
    public string Duration { get; set; } = "";

    /// <summary>The "Company benefits" line from the key details.</summary>
    public string CompanyBenefits { get; set; } = "";

    /// <summary>The listing's reference number. Advertisers can type anything here, for example "81084" or "Summer Duty Manager".</summary>
    public string ReferenceNumber { get; set; } = "";

    /// <summary>The "Application details" text, for example "Apply online for this role or contact Stella Cliffe for more information."</summary>
    public string ApplicationDetails { get; set; } = "";

    /// <summary>The employer profile's "Contact person".</summary>
    public string ContactPerson { get; set; } = "";

    /// <summary>Any other key details Trade Me showed, one "Label: value" per line, so nothing is lost if a listing has extras.</summary>
    public string OtherDetails { get; set; } = "";

    public DateTime ScrapedAt { get; set; }
    public DateTime ReceivedAt { get; set; }
}
