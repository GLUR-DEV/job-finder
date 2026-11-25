using JobFinder.Web.Data;
using Microsoft.AspNetCore.Components;

namespace JobFinder.Web.Components;

/// <summary>
/// Base for the Jobs and Favourites pages. Reads the shared filters from the query string
/// (for example "/?location=Penrose%2C%20Auckland&amp;workType=Part%20Time"), which is how the Map page
/// hands its filters over when a district is clicked.
/// </summary>
public abstract class FilteredPageBase : ComponentBase
{
    private string? lastQuery;

    [Parameter, SupplyParameterFromQuery(Name = "keyword")] public string? QueryKeyword { get; set; }
    [Parameter, SupplyParameterFromQuery(Name = "location")] public string? QueryLocation { get; set; }
    [Parameter, SupplyParameterFromQuery(Name = "workType")] public string? QueryWorkType { get; set; }
    [Parameter, SupplyParameterFromQuery(Name = "classification")] public string? QueryClassification { get; set; }
    [Parameter, SupplyParameterFromQuery(Name = "subClass")] public string? QuerySubClass { get; set; }
    [Parameter, SupplyParameterFromQuery(Name = "listed")] public int? QueryListed { get; set; }
    [Parameter, SupplyParameterFromQuery(Name = "applicant")] public string? QueryApplicant { get; set; }
    [Parameter, SupplyParameterFromQuery(Name = "contact")] public string? QueryContact { get; set; }
    [Parameter, SupplyParameterFromQuery(Name = "hours")] public string? QueryHours { get; set; }

    /// <summary>The filters currently applied on the page.</summary>
    protected JobFilter Filter { get; } = new();

    /// <summary>
    /// Loads <see cref="Filter"/> from the query string. Returns true when the query string is different
    /// from last time (always true on the first call), so the page knows to reload.
    /// </summary>
    protected bool ApplyQuery()
    {
        var signature = string.Join("|", QueryKeyword, QueryLocation, QueryWorkType, QueryClassification,
            QuerySubClass, QueryListed, QueryApplicant, QueryContact, QueryHours);
        if (signature == lastQuery) return false;

        lastQuery = signature;
        Filter.Load(QueryKeyword, QueryLocation, QueryWorkType, QueryClassification, QuerySubClass,
                    QueryListed, QueryApplicant, QueryContact, QueryHours);
        return true;
    }
}
