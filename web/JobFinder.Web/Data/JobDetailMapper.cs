using System.Globalization;

namespace JobFinder.Web.Data;

public static class JobDetailMapper
{
    public static void Apply(JobDetail detail, JobDetailDto d, DateTime nowUtc)
    {
        detail.Url = Clean(d.Url);
        detail.Title = Clean(d.Title);
        detail.Advertiser = Clean(d.Advertiser);
        detail.AdvertiserUrl = Clean(d.AdvertiserUrl);
        detail.Location = Clean(d.Location);
        detail.Classification = Clean(d.Classification);
        detail.WorkType = Clean(d.WorkType);
        detail.Salary = Clean(d.Salary);
        detail.Posted = Clean(d.Posted);
        detail.ApplicationVolume = Clean(d.ApplicationVolume);
        detail.ApplyType = Clean(d.ApplyType);
        detail.ApplyUrl = Clean(d.ApplyUrl);
        detail.Badges = Clean(d.Badges);
        detail.SkillsSummary = Clean(d.SkillsSummary);
        detail.SkillsMatched = Clean(d.SkillsMatched);
        detail.SkillsSuggested = Clean(d.SkillsSuggested);
        detail.EmployerQuestions = Clean(d.EmployerQuestions);
        detail.Emails = Clean(d.Emails);
        detail.Phones = Clean(d.Phones);
        detail.Description = Clean(d.Description);

        detail.ScrapedAt = DateTime.TryParse(d.ScrapedAt, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt) ? dt : nowUtc;
        detail.ReceivedAt = nowUtc;
    }

    private static string Clean(string? s) => s?.Trim() ?? "";
}
