using System.Globalization;

namespace JobFinder.Web.Data;

public static class JobMapper
{
    public static bool IsTradeMe(JobDto d) =>
        string.Equals(d.Source?.Trim(), JobSources.TradeMe, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The id a job is stored under, or null when it has none. Trade Me ids get a prefix so they can never clash with SEEK's.
    /// </summary>
    public static string? KeyOf(JobDto d)
    {
        var id = d.Id?.Trim();
        if (string.IsNullOrEmpty(id)) return null;
        return IsTradeMe(d) ? JobSources.TradeMeIdPrefix + id : id;
    }

    /// <summary>Copies an incoming job onto an entity (new or existing).</summary>
    public static void Apply(Job job, JobDto d, DateTime nowUtc)
    {
        job.Title = Clean(d.Title);
        job.Company = Clean(d.Company);
        job.Location = Clean(d.Location);
        job.Listed = Clean(d.Listed);
        job.Description = Clean(d.Description);
        job.Url = Clean(d.Url);

        job.ScrapedAt = ParseDate(d.ScrapedAt) ?? nowUtc;
        job.ReceivedAt = nowUtc;

        if (IsTradeMe(d)) ApplyTradeMe(job, d);
        else ApplySeek(job, d);
    }

    private static void ApplySeek(Job job, JobDto d)
    {
        job.Source = JobSources.Seek;
        job.Salary = Clean(d.Salary);
        job.WorkType = Clean(d.WorkType);
        job.Classification = Clean(d.Classification);
        job.SubClass = Clean(d.SubClass);
        job.ListingStatus = Clean(d.ListingStatus);
        job.ApplicantStrength = Clean(d.ApplicantStrength);
        job.Badges = Clean(d.Badges);
        job.AdType = Clean(d.AdType);
        job.Section = Clean(d.Section);
        job.ListedDate = ListedParser.Parse(job.Listed, job.ScrapedAt);
    }

    /// <summary>
    /// Trade Me cards have no applicant strength, badges, ad type or section. Pay comes from the card's pay field,
    /// or failing that from a figure found in the teaser text. Category and work type are merged with SEEK's names.
    /// </summary>
    private static void ApplyTradeMe(Job job, JobDto d)
    {
        job.Source = JobSources.TradeMe;

        var pay = Clean(d.Pay);
        job.Salary = pay != "" ? pay : Clean(d.PayInText);

        job.WorkType = TradeMeTaxonomy.WorkType(d.WorkType);
        (job.Classification, job.SubClass) = TradeMeTaxonomy.Classify(d.Category, d.SubCategory, d.SubSubCategory);
        job.SourceCategory = string.Join("/", new[] { d.Category, d.SubCategory, d.SubSubCategory }
            .Select(s => Clean(s)).Where(s => s != ""));

        job.Region = TradeMeTaxonomy.Humanize(d.Region);
        job.District = TradeMeTaxonomy.Humanize(d.District);

        job.Tags = Clean(d.Tags);
        job.BulletPoints = Clean(d.BulletPoints);
        job.IsPromoted = d.IsPromoted ?? false;
        job.QuickApply = d.QuickApply ?? false;
        job.ImageUrl = Clean(d.ImageUrl);
        job.LogoUrl = Clean(d.LogoUrl);

        // SEEK-only fields stay empty.
        job.ApplicantStrength = "";
        job.Badges = "";
        job.ListingStatus = "";
        job.AdType = "";
        job.Section = "";

        // Trade Me gives the exact listing time, which beats working it out from "10 days ago".
        job.ListedDate = ParseDate(d.ListedAt) ?? ListedParser.Parse(job.Listed, job.ScrapedAt);
    }

    private static string Clean(string? s) => s?.Trim() ?? "";

    private static DateTime? ParseDate(string? s) =>
        DateTime.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt)
            ? dt : null;
}
