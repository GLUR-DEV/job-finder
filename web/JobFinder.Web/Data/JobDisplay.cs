namespace JobFinder.Web.Data;

/// <summary>One label and value line in a details panel.</summary>
public record Row(string Label, string Value, bool Multiline = false, bool Link = false);

/// <summary>Builds the rows shown when a card is clicked.</summary>
public static class JobDisplay
{
    public static string Utc(DateTime d) => d.ToString("yyyy-MM-dd HH:mm") + " UTC";

    /// <summary>Every field scraped from the search results card.</summary>
    public static IEnumerable<Row> JobRows(Job j) => new[]
    {
        new Row("Job ID", j.Id),
        new Row("Company", j.Company),
        new Row("Location", j.Location),
        new Row("Salary", j.Salary),
        new Row("Work type", j.WorkType),
        new Row("Classification", j.Classification),
        new Row("Sub-classification", j.SubClass),
        new Row("Applicant strength", j.ApplicantStrength),
        new Row("Badges", j.Badges),
        new Row("Listed (as shown on SEEK)", j.Listed),
        new Row("Listed (approximate date)", j.ListedDate is { } d ? d.ToString("yyyy-MM-dd") : ""),
        new Row("Listing status", j.ListingStatus),
        new Row("Ad type", j.AdType),
        new Row("Section", j.Section),
        new Row("Description", j.Description, Multiline: true),
        new Row("Job URL", j.Url, Link: true),
        new Row("Scraped", Utc(j.ScrapedAt)),
        new Row("Received by this site", Utc(j.ReceivedAt)),
    };

    /// <summary>Every field scraped from the job's own page.</summary>
    public static IEnumerable<Row> DetailRows(JobDetail d) => new[]
    {
        new Row("Title on job page", d.Title),
        new Row("Advertiser", d.Advertiser),
        new Row("Advertiser's other jobs", d.AdvertiserUrl, Link: true),
        new Row("Location", d.Location),
        new Row("Classification", d.Classification),
        new Row("Work type", d.WorkType),
        new Row("Salary", d.Salary),
        new Row("Posted", d.Posted),
        new Row("Application volume", d.ApplicationVolume),
        new Row("Badges", d.Badges),
        new Row("Apply button", d.ApplyType),
        new Row("Apply link", d.ApplyUrl, Link: true),
        new Row("Skills summary", d.SkillsSummary),
        new Row("Skills that match your profile", d.SkillsMatched),
        new Row("Skills to add to your profile", d.SkillsSuggested),
        new Row("Contact emails", d.Emails),
        new Row("Contact phone numbers", d.Phones),
        new Row("Hours mentioned in the ad (detected)", string.Join("; ", HoursDetector.Matches(d.Description))),
        new Row("Employer questions", d.EmployerQuestions, Multiline: true),
        new Row("Full job ad", d.Description, Multiline: true),
        new Row("Job page URL", d.Url, Link: true),
        new Row("Details scraped", Utc(d.ScrapedAt)),
        new Row("Details received by this site", Utc(d.ReceivedAt)),
    };
}
