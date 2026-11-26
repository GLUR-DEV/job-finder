using System.Text.Json;

namespace JobFinder.Web.Data;

/// <summary>
/// Outlines for the nine Trade Me jobs districts in Auckland, read once from Data/trademe-districts.geojson.
/// Trade Me only places a job in one of these large districts (the listing URL's district part, stored in
/// <see cref="Job.District"/>), so the Trade Me map uses these shapes instead of suburbs.
///
/// The shapes come from Statistics NZ's 2006 territorial authority boundaries, simplified so the page stays light.
/// Auckland City is the mainland only: the 2006 Auckland City boundary also held the Hauraki Gulf islands,
/// which are split out into "Waiheke Island" (Waiheke and its neighbouring islets) and "Hauraki Gulf Islands"
/// (Great Barrier, Little Barrier, Rangitoto, Motutapu, Motuihe, Rākino and the rest).
/// </summary>
public class TradeMeBoundaries
{
    /// <summary>Only jobs in this Trade Me region are drawn. Jobs in other regions are reported as not on the map.</summary>
    public const string RegionName = "Auckland";

    /// <summary>One district outline. <see cref="Name"/> matches <see cref="Job.District"/> (for example "North Shore City").</summary>
    public sealed record Outline(string Name, string GeoJson);

    private readonly Dictionary<string, Outline> byName = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<Outline> All => byName.Values;

    public TradeMeBoundaries(IWebHostEnvironment env, ILogger<TradeMeBoundaries> log)
    {
        var path = Path.Combine(env.ContentRootPath, "Data", "trademe-districts.geojson");
        if (!File.Exists(path))
        {
            log.LogWarning("Trade Me district outlines not found at {Path}. The Trade Me map will be empty.", path);
            return;
        }

        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        foreach (var feature in doc.RootElement.GetProperty("features").EnumerateArray())
        {
            var name = feature.GetProperty("properties").GetProperty("name").GetString();
            if (string.IsNullOrWhiteSpace(name)) continue;
            byName[name] = new Outline(name, feature.GetProperty("geometry").GetRawText());
        }

        log.LogInformation("Loaded {Count} Trade Me district outlines", byName.Count);
    }

    /// <summary>Finds a district by the name stored on a job, ignoring case.</summary>
    public bool TryGet(string district, out Outline found) => byName.TryGetValue(district.Trim(), out found!);
}
