using System.Globalization;

namespace JobFinder.Web.Data;

/// <summary>
/// The filters shared by the Jobs, Favourites and Map pages.
/// <see cref="Contact"/> and <see cref="Hours"/> only apply to favourites.
/// </summary>
public class JobFilter
{
    private static readonly int[] ListedChoices = { 0, 1, 3, 7, 14, 30 };
    private static readonly string[] ApplicantChoices = { "", "strong", "very", "both" };
    private static readonly string[] ContactChoices = { "", "phone", "email", "both", "none" };
    private static readonly string[] HoursChoices = { "", "yes", "no" };

    public string Keyword { get; set; } = "";
    public string Location { get; set; } = "";
    public string WorkType { get; set; } = "";
    public string Classification { get; set; } = "";
    public string SubClass { get; set; } = "";
    public int ListedDays { get; set; }

    /// <summary>"", "strong", "very" or "both".</summary>
    public string Applicant { get; set; } = "";

    /// <summary>Favourites only: "", "phone", "email", "both" or "none".</summary>
    public string Contact { get; set; } = "";

    /// <summary>Favourites only: "", "yes" or "no".</summary>
    public string Hours { get; set; } = "";

    public bool HasJobFilters =>
        Keyword.Trim() != "" || Location != "" || WorkType != "" || Classification != "" ||
        SubClass != "" || ListedDays != 0 || Applicant != "";

    public bool HasFavouriteFilters => Contact != "" || Hours != "";

    public bool Has(bool includeFavourite) => HasJobFilters || (includeFavourite && HasFavouriteFilters);

    public void Clear()
    {
        Keyword = Location = WorkType = Classification = SubClass = Applicant = Contact = Hours = "";
        ListedDays = 0;
    }

    /// <summary>Fills the filter from query string values, ignoring anything it doesn't recognise.</summary>
    public void Load(string? keyword, string? location, string? workType, string? classification,
                     string? subClass, int? listed, string? applicant, string? contact, string? hours)
    {
        Keyword = keyword?.Trim() ?? "";
        Location = location ?? "";
        WorkType = workType ?? "";
        Classification = classification ?? "";
        SubClass = subClass ?? "";
        ListedDays = listed is { } l && ListedChoices.Contains(l) ? l : 0;
        Applicant = applicant is not null && ApplicantChoices.Contains(applicant) ? applicant : "";
        Contact = contact is not null && ContactChoices.Contains(contact) ? contact : "";
        Hours = hours is not null && HoursChoices.Contains(hours) ? hours : "";
    }

    /// <summary>
    /// Builds a query string ("?keyword=...&amp;location=...") for linking to another page with the same filters.
    /// Pass <paramref name="location"/> to replace the location.
    /// </summary>
    public string ToQueryString(bool includeFavourite, string? location = null)
    {
        var pairs = new List<string>();

        void Add(string key, string? value)
        {
            if (!string.IsNullOrEmpty(value)) pairs.Add($"{key}={Uri.EscapeDataString(value)}");
        }

        Add("keyword", Keyword.Trim());
        Add("location", location ?? Location);
        Add("workType", WorkType);
        Add("classification", Classification);
        Add("subClass", SubClass);
        if (ListedDays > 0) Add("listed", ListedDays.ToString(CultureInfo.InvariantCulture));
        Add("applicant", Applicant);
        if (includeFavourite)
        {
            Add("contact", Contact);
            Add("hours", Hours);
        }

        return pairs.Count == 0 ? "" : "?" + string.Join("&", pairs);
    }
}
