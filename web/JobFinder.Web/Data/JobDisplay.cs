namespace JobFinder.Web.Data;

/// <summary>One label and value line in a details panel.</summary>
public record Row(string Label, string Value, bool Multiline = false, bool Link = false);

/// <summary>Builds the rows shown when a card is clicked.</summary>
public static class JobDisplay
{
    public static string Utc(DateTime d) => d.ToString("yyyy-MM-dd HH:mm") + " UTC";

    /// <summary>Every field scraped from the search results card. SEEK and Trade Me jobs each show their own fields.</summary>
    public static IEnumerable<Row> JobRows(Job j)
    {
        var tradeMe = j.Source == JobSources.TradeMe;
        var site = JobSources.Label(j.Source);

        var rows = new List<Row>
        {
            new("Site", site),
            new("Job ID", j.Id),
            new("Company", j.Company),
            new("Location", j.Location),
            new("Salary", j.Salary),
            new("Work type", j.WorkType),
            new("Classification", j.Classification),
            new("Sub-classification", j.SubClass),
        };

        if (tradeMe)
        {
            rows.Add(new("Trade Me category", j.SourceCategory));
            rows.Add(new("Region", j.Region));
            rows.Add(new("District", j.District));
            rows.Add(new("Tags", j.Tags));
            rows.Add(new("Promoted listing", j.IsPromoted ? "Yes" : "No"));
            rows.Add(new("Quick apply", j.QuickApply ? "Yes" : "No"));
        }
        else
        {
            rows.Add(new("Applicant strength", j.ApplicantStrength));
            rows.Add(new("Badges", j.Badges));
        }

        rows.Add(new($"Listed (as shown on {site})", j.Listed));
        rows.Add(new(tradeMe ? "Listed (date)" : "Listed (approximate date)", j.ListedDate is { } d ? d.ToString("yyyy-MM-dd") : ""));

        if (!tradeMe)
        {
            rows.Add(new("Listing status", j.ListingStatus));
            rows.Add(new("Ad type", j.AdType));
            rows.Add(new("Section", j.Section));
        }

        rows.Add(new("Description", j.Description, Multiline: true));
        if (tradeMe)
        {
            rows.Add(new("Bullet points", j.BulletPoints, Multiline: true));
            rows.Add(new("Photo", j.ImageUrl, Link: true));
            rows.Add(new("Advertiser logo", j.LogoUrl, Link: true));
        }

        rows.Add(new("Job URL", j.Url, Link: true));
        rows.Add(new("Scraped", Utc(j.ScrapedAt)));
        rows.Add(new("Received by this site", Utc(j.ReceivedAt)));
        return rows;
    }

    /// <summary>Every field scraped from the job's own page. SEEK and Trade Me pages each show their own fields.</summary>
    public static IEnumerable<Row> DetailRows(JobDetail d, string source = JobSources.Seek) =>
        source == JobSources.TradeMe ? TradeMeDetailRows(d) : SeekDetailRows(d);

    private static IEnumerable<Row> TradeMeDetailRows(JobDetail d) => new[]
    {
        new Row("Title on job page", d.Title),
        new Row("Advertiser", d.Advertiser),
        new Row("Advertiser's other listings", d.AdvertiserUrl, Link: true),
        new Row("Contact person", d.ContactPerson),
        new Row("Location", d.Location),
        new Row("Job type", d.WorkType),
        new Row("Duration", d.Duration),
        new Row("Approximate pay", d.Salary),
        new Row("Company benefits", d.CompanyBenefits),
        new Row("Other key details", d.OtherDetails, Multiline: true),
        new Row("Listed (as shown on Trade Me)", d.Posted),
        new Row("Reference #", d.ReferenceNumber),
        new Row("Apply button", d.ApplyType),
        new Row("Application details", d.ApplicationDetails, Multiline: true),
        new Row("Contact emails", d.Emails),
        new Row("Contact phone numbers", d.Phones),
        new Row("Hours mentioned in the ad (detected)", string.Join("; ", HoursDetector.Matches(d.Description))),
        new Row("Full job ad", d.Description, Multiline: true),
        new Row("Job page URL", d.Url, Link: true),
        new Row("Details scraped", Utc(d.ScrapedAt)),
        new Row("Details received by this site", Utc(d.ReceivedAt)),
    };

    private static IEnumerable<Row> SeekDetailRows(JobDetail d) => new[]
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
