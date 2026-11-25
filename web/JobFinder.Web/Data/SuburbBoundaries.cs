using System.Globalization;
using System.Text;
using System.Text.Json;

namespace JobFinder.Web.Data;

/// <summary>
/// Official suburb outlines for Auckland, read once from Data/auckland-suburbs.geojson (LINZ "NZ Suburbs and Localities").
/// A job location such as "Penrose, Auckland" is matched on its suburb name, ignoring case and macrons.
/// If a suburb has no name match, one of the "additional names" LINZ lists for it is used, but only when that
/// name belongs to exactly one suburb. The file also holds a merged outline for the town of Whangaparāoa, which LINZ
/// splits into several suburbs. Anything else is left for the OpenStreetMap lookup.
/// </summary>
public class SuburbBoundaries
{
    /// <summary>Starts <see cref="District.MatchedName"/> for outlines that came from this file.</summary>
    public const string MatchedPrefix = "LINZ: ";

    /// <summary>
    /// A job whose location is just this (no suburb) can't be placed on the map. The map never draws the whole region,
    /// so these jobs are left out of the map page and its outline lookup.
    /// </summary>
    public const string RegionName = "Auckland";

    public static bool IsRegionOnly(string location) =>
        string.Equals(location.Trim(), RegionName, StringComparison.OrdinalIgnoreCase);

    private sealed record Suburb(string Name, string GeoJson, double Lat, double Lon);

    private readonly Dictionary<string, Suburb> byName = new();
    private readonly Dictionary<string, List<Suburb>> byAlias = new();

    public SuburbBoundaries(IWebHostEnvironment env, ILogger<SuburbBoundaries> log)
    {
        var path = Path.Combine(env.ContentRootPath, "Data", "auckland-suburbs.geojson");
        if (!File.Exists(path))
        {
            log.LogWarning("Suburb outlines not found at {Path}. Only OpenStreetMap will be used.", path);
            return;
        }

        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        foreach (var feature in doc.RootElement.GetProperty("features").EnumerateArray())
        {
            var props = feature.GetProperty("properties");
            var name = props.GetProperty("name").GetString();
            if (string.IsNullOrWhiteSpace(name)) continue;

            var geometry = feature.GetProperty("geometry");
            double minLon = 180, maxLon = -180, minLat = 90, maxLat = -90;
            Extend(geometry.GetProperty("coordinates"), ref minLon, ref maxLon, ref minLat, ref maxLat);

            var suburb = new Suburb(name, geometry.GetRawText(), (minLat + maxLat) / 2, (minLon + maxLon) / 2);

            var key = Normalise(name);
            byName.TryAdd(key, suburb);

            if (props.TryGetProperty("additional_name", out var extra) && extra.ValueKind == JsonValueKind.String)
            {
                foreach (var alias in extra.GetString()!.Split(','))
                {
                    var a = Normalise(alias);
                    if (a == "" || a == key) continue;
                    if (!byAlias.TryGetValue(a, out var list)) byAlias[a] = list = new List<Suburb>();
                    list.Add(suburb);
                }
            }
        }

        log.LogInformation("Loaded {Count} official suburb outlines", byName.Count);
    }

    /// <summary>Finds the official outline for a job location, or null when there isn't a safe match.</summary>
    public District? Find(string location)
    {
        var parts = location.Split(',').Select(p => p.Trim()).Where(p => p != "").ToArray();

        // The outlines only cover Auckland, so "Newmarket, Waikato" must not pick up Auckland's Newmarket.
        if (parts.Length < 2 || !parts[^1].Equals("Auckland", StringComparison.OrdinalIgnoreCase)) return null;

        var key = Normalise(parts[0]);
        var viaAlias = false;

        if (!byName.TryGetValue(key, out var suburb))
        {
            if (byAlias.TryGetValue(key, out var list) && list.Count == 1)
            {
                suburb = list[0];
                viaAlias = true;
            }
            else
            {
                return null;
            }
        }

        return new District
        {
            Name = location,
            Status = DistrictStatus.Outline,
            GeoJson = suburb.GeoJson,
            Lat = suburb.Lat,
            Lon = suburb.Lon,
            MatchedName = MatchedPrefix + suburb.Name + (viaAlias ? " (matched on an alternative name)" : ""),
            LookedUpAt = DateTime.UtcNow
        };
    }

    private static string Normalise(string text)
    {
        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Walks GeoJSON coordinates and widens the bounding box to include each [lon, lat] pair.</summary>
    private static void Extend(JsonElement e, ref double minLon, ref double maxLon, ref double minLat, ref double maxLat)
    {
        if (e.ValueKind != JsonValueKind.Array) return;

        if (e.GetArrayLength() >= 2 && e[0].ValueKind == JsonValueKind.Number)
        {
            var lon = e[0].GetDouble();
            var lat = e[1].GetDouble();
            minLon = Math.Min(minLon, lon);
            maxLon = Math.Max(maxLon, lon);
            minLat = Math.Min(minLat, lat);
            maxLat = Math.Max(maxLat, lat);
            return;
        }

        foreach (var child in e.EnumerateArray())
            Extend(child, ref minLon, ref maxLon, ref minLat, ref maxLat);
    }
}
