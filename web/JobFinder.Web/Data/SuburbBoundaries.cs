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

    /// <summary>
    /// Job locations that LINZ only lists as an alternative name shared by several suburbs, so they can't be matched
    /// automatically. Each one is pointed at the single suburb whose outline best stands in for it. Keys are
    /// compared after <see cref="Normalise"/>, so case and macrons don't matter.
    /// </summary>
    private static readonly Dictionary<string, string> AliasOverrides = new()
    {
        ["newton"] = "Eden Terrace",
        ["Auckland City"] = "Auckland Central",
    };

    /// <summary>
    /// Job locations that are really several LINZ suburbs. The listed suburbs' outlines are combined into one shape
    /// that is shown under the group's name. Names are compared after <see cref="Normalise"/>. Any member missing from
    /// the geojson is skipped, and a suburb already in the file under the group's own name is included too.
    /// Add or remove names here to change what a group covers.
    /// </summary>
    private static readonly Dictionary<string, string[]> Groups = new()
    {
        ["Whangaparāoa"] = new[]
        {
            "Army Bay", "Arkles Bay", "Gulf Harbour", "Manly", "Matakatia", "Stanmore Bay", "Tindalls Beach",
        },
    };

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

        AddGroups(log);

        log.LogInformation("Loaded {Count} official suburb outlines", byName.Count);
    }

    /// <summary>Builds each entry of <see cref="Groups"/> into one multi-part outline and registers it by name.</summary>
    private void AddGroups(ILogger log)
    {
        foreach (var (groupName, memberNames) in Groups)
        {
            var key = Normalise(groupName);
            var parts = new List<Suburb>();
            var missing = new List<string>();

            // A suburb already in the file under the group's own name is part of the group too.
            if (byName.TryGetValue(key, out var existing)) parts.Add(existing);

            foreach (var member in memberNames)
            {
                if (byName.TryGetValue(Normalise(member), out var found)) parts.Add(found);
                else missing.Add(member);
            }

            if (missing.Count > 0)
                log.LogWarning("Group {Group}: no outline found for {Missing}", groupName, string.Join(", ", missing));
            if (parts.Count == 0) continue;

            byName[key] = Combine(groupName + " (combined suburbs)", parts);
        }
    }

    /// <summary>Joins the outlines of several suburbs into one MultiPolygon. The shapes sit side by side, so no merging of edges is needed.</summary>
    private static Suburb Combine(string name, IReadOnlyList<Suburb> parts)
    {
        double minLon = 180, maxLon = -180, minLat = 90, maxLat = -90;

        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("type", "MultiPolygon");
            w.WritePropertyName("coordinates");
            w.WriteStartArray();

            foreach (var part in parts)
            {
                using var doc = JsonDocument.Parse(part.GeoJson);
                var coords = doc.RootElement.GetProperty("coordinates");
                Extend(coords, ref minLon, ref maxLon, ref minLat, ref maxLat);

                if (doc.RootElement.GetProperty("type").GetString() == "MultiPolygon")
                    foreach (var polygon in coords.EnumerateArray()) polygon.WriteTo(w);
                else
                    coords.WriteTo(w);
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return new Suburb(name, Encoding.UTF8.GetString(stream.ToArray()), (minLat + maxLat) / 2, (minLon + maxLon) / 2);
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
            if (AliasOverrides.TryGetValue(key, out var target) && byName.TryGetValue(Normalise(target), out var forced))
            {
                suburb = forced;
                viaAlias = true;
            }
            else if (byAlias.TryGetValue(key, out var list) && list.Count == 1)
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
