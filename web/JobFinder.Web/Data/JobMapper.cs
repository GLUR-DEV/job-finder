using System.Globalization;

namespace JobFinder.Web.Data;

public static class JobMapper
{
    /// <summary>Copies an incoming job onto an entity (new or existing).</summary>
    public static void Apply(Job job, JobDto d, DateTime nowUtc)
    {
        job.Title = Clean(d.Title);
        job.Company = Clean(d.Company);
        job.Location = Clean(d.Location);
        job.Salary = Clean(d.Salary);
        job.WorkType = Clean(d.WorkType);
        job.Classification = Clean(d.Classification);
        job.SubClass = Clean(d.SubClass);
        job.Listed = Clean(d.Listed);
        job.ListingStatus = Clean(d.ListingStatus);
        job.ApplicantStrength = Clean(d.ApplicantStrength);
        job.Badges = Clean(d.Badges);
        job.AdType = Clean(d.AdType);
        job.Section = Clean(d.Section);
        job.Description = Clean(d.Description);
        job.Url = Clean(d.Url);

        job.ScrapedAt = ParseDate(d.ScrapedAt) ?? nowUtc;
        job.ListedDate = ListedParser.Parse(job.Listed, job.ScrapedAt);
        job.ReceivedAt = nowUtc;
    }

    private static string Clean(string? s) => s?.Trim() ?? "";

    private static DateTime? ParseDate(string? s) =>
        DateTime.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt)
            ? dt : null;
}
