using System.ComponentModel.DataAnnotations;

namespace JobFinder.Web.Data;

public static class DistrictStatus
{
    /// <summary>OpenStreetMap returned a boundary for this location.</summary>
    public const string Outline = "outline";

    /// <summary>OpenStreetMap only knew a single point, so the map shows a circle.</summary>
    public const string Point = "point";

    /// <summary>Nothing usable was found. Not drawn on the map.</summary>
    public const string NotFound = "notfound";
}

/// <summary>The map shape for one job location (for example "Penrose, Auckland"), cached from OpenStreetMap.</summary>
public class District
{
    /// <summary>Exactly the location text SEEK gave the jobs.</summary>
    [Key]
    public string Name { get; set; } = "";

    public string Status { get; set; } = DistrictStatus.NotFound;

    /// <summary>GeoJSON geometry (Polygon or MultiPolygon). Empty unless the status is outline.</summary>
    public string GeoJson { get; set; } = "";

    public double? Lat { get; set; }
    public double? Lon { get; set; }

    /// <summary>OpenStreetMap's name for what it matched, so a wrong match is easy to spot.</summary>
    public string MatchedName { get; set; } = "";

    public DateTime LookedUpAt { get; set; }
}
