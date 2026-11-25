using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace JobFinder.Web.Data;

/// <summary>
/// Finds a map outline for every job location and caches the answer. Auckland suburbs use the official outlines in
/// <see cref="SuburbBoundaries"/>. Everything else is looked up once on OpenStreetMap's Nominatim search.
/// The Nominatim part follows Nominatim's usage policy: one request per second at most, an identifying User-Agent, and results are
/// stored so each location is only ever looked up once. Runs in the background, independent of any browser page.
/// </summary>
public class DistrictLookupService(
    IDbContextFactory<JobDbContext> jobDbFactory,
    IDbContextFactory<DistrictDbContext> districtDbFactory,
    IHttpClientFactory httpFactory,
    SuburbBoundaries suburbs,
    ILogger<DistrictLookupService> log)
{
    private readonly object sync = new();

    public bool Running { get; private set; }
    public int Total { get; private set; }
    public int Done { get; private set; }
    public string LastError { get; private set; } = "";

    /// <summary>Raised on progress and when a run ends. May be raised from a background thread.</summary>
    public event Action? Changed;

    /// <summary>
    /// Swaps in the official suburb outline for any cached location that has one and isn't using it yet
    /// (for example a circle or "not found" saved by an earlier version). Needs no internet, so it runs at startup.
    /// </summary>
    public void ApplyOfficialBoundaries()
    {
        using var db = districtDbFactory.CreateDbContext();
        var changed = 0;

        foreach (var row in db.Districts.ToList())
        {
            if (row.MatchedName.StartsWith(SuburbBoundaries.MatchedPrefix, StringComparison.Ordinal)) continue;
            if (suburbs.Find(row.Name) is not { } official) continue;

            row.Status = official.Status;
            row.GeoJson = official.GeoJson;
            row.Lat = official.Lat;
            row.Lon = official.Lon;
            row.MatchedName = official.MatchedName;
            row.LookedUpAt = official.LookedUpAt;
            changed++;
        }

        if (changed > 0) db.SaveChanges();
    }

    /// <summary>Looks up any locations that don't have a cached result yet. Does nothing if a run is already going.</summary>
    /// <param name="retryNotFound">Also forget earlier "nothing found" results and try those again.</param>
    public void Start(bool retryNotFound = false)
    {
        lock (sync)
        {
            if (Running) return;
            Running = true;
            Total = 0;
            Done = 0;
            LastError = "";
        }
        Changed?.Invoke();
        _ = Task.Run(() => RunAsync(retryNotFound));
    }

    private async Task RunAsync(bool retryNotFound)
    {
        try
        {
            await using var jobDb = await jobDbFactory.CreateDbContextAsync();
            await using var districtDb = await districtDbFactory.CreateDbContextAsync();

            if (retryNotFound)
                await districtDb.Districts.Where(d => d.Status == DistrictStatus.NotFound).ExecuteDeleteAsync();

            var wanted = (await jobDb.Jobs.Where(j => j.Location != "").Select(j => j.Location).Distinct().ToListAsync())
                .Where(n => !SuburbBoundaries.IsRegionOnly(n))   // "Auckland" on its own is never drawn
                .ToList();
            var known = (await districtDb.Districts.Select(d => d.Name).ToListAsync()).ToHashSet();
            var missing = wanted.Where(n => !known.Contains(n)).OrderBy(n => n).ToList();

            Total = missing.Count;
            Changed?.Invoke();

            var client = httpFactory.CreateClient("nominatim");
            foreach (var name in missing)
            {
                var official = suburbs.Find(name);
                var district = official ?? await LookupAsync(client, name);
                districtDb.Districts.Add(district);
                await districtDb.SaveChangesAsync();

                Done++;
                Changed?.Invoke();

                // Nominatim allows at most one request per second. The official outlines need no request, so no wait.
                if (official is null) await Task.Delay(1100);
            }
        }
        catch (Exception ex)
        {
            // Network problems and rate limits stop the run. Nothing is cached for the location that failed,
            // so it is simply tried again next time.
            LastError = ex.Message;
            log.LogWarning(ex, "District lookup stopped early");
        }
        finally
        {
            lock (sync) Running = false;
            Changed?.Invoke();
        }
    }

    private static async Task<District> LookupAsync(HttpClient client, string name)
    {
        var parts = name.Split(',').Select(p => p.Trim()).Where(p => p != "").ToArray();
        var region = parts.Length > 1 ? parts[^1] : "";
        var query = string.Join(", ", parts) + ", New Zealand";

        var url = "search?format=jsonv2&polygon_geojson=1&polygon_threshold=0.0003&limit=5&countrycodes=nz&q="
                  + Uri.EscapeDataString(query);

        // Stops "Mount Wellington, Auckland" matching somewhere in Wellington, and similar mix-ups.
        if (region.Equals("Auckland", StringComparison.OrdinalIgnoreCase))
            url += "&viewbox=174.15,-36.35,175.45,-37.45&bounded=1";

        using var res = await client.GetAsync(url);
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"OpenStreetMap lookup returned {(int)res.StatusCode} ({res.ReasonPhrase})");

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());

        JsonElement? polygon = null, point = null;
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            var cls = r.TryGetProperty("category", out var c1) ? c1.GetString()
                    : r.TryGetProperty("class", out var c2) ? c2.GetString() : null;
            if (!r.TryGetProperty("geojson", out var g)) continue;
            var type = g.TryGetProperty("type", out var t) ? t.GetString() : null;

            if (polygon is null && cls is "boundary" or "place" && type is "Polygon" or "MultiPolygon")
                polygon = r;
            else if (point is null && cls == "place" && type == "Point")
                point = r;
        }

        var chosen = polygon ?? point;
        if (chosen is not { } m)
        {
            return new District { Name = name, Status = DistrictStatus.NotFound, LookedUpAt = DateTime.UtcNow };
        }

        return new District
        {
            Name = name,
            Status = polygon is not null ? DistrictStatus.Outline : DistrictStatus.Point,
            GeoJson = polygon is not null ? m.GetProperty("geojson").GetRawText() : "",
            Lat = double.Parse(m.GetProperty("lat").GetString()!, CultureInfo.InvariantCulture),
            Lon = double.Parse(m.GetProperty("lon").GetString()!, CultureInfo.InvariantCulture),
            MatchedName = m.TryGetProperty("display_name", out var dn) ? dn.GetString() ?? "" : "",
            LookedUpAt = DateTime.UtcNow
        };
    }
}
