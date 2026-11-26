using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace JobFinder.Web.Data;

/// <summary>Body of POST /api/watch/url.</summary>
public record WatchUrlDto(string? Url);

/// <summary>Body of POST /api/watch/results: everything the extension scraped for one scan.</summary>
public record WatchResultDto(string? RunId, List<JobDto>? Jobs, int Pages, string? Error);

/// <summary>What the extension is told to do when a scan is due. <paramref name="Source"/> is the site being scanned.</summary>
public record WatchClaim(string RunId, string Url, string Source);

/// <summary>A search link the watch accepted: which site it is for and the tidied link.</summary>
public record WatchLink(string Source, string Url);

/// <summary>Checks and tidies the SEEK or Trade Me search link the watch rescans.</summary>
public static partial class WatchUrl
{
    [GeneratedRegex(@"^([^.]+\.)*seek\.(com(\.au)?|co\.nz)$", RegexOptions.IgnoreCase)]
    private static partial Regex SeekHost();

    [GeneratedRegex(@"^([^.]+\.)*trademe\.co\.nz$", RegexOptions.IgnoreCase)]
    private static partial Regex TradeMeHost();

    /// <summary>
    /// Returns the site and the link without its page number (so scans always start at page 1) and fragment,
    /// or null if it isn't an https SEEK or Trade Me jobs search link.
    /// </summary>
    public static WatchLink? Parse(string? raw)
    {
        if (!Uri.TryCreate(raw?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;

        var path = uri.AbsolutePath;
        string source;
        if (SeekHost().IsMatch(uri.Host))
        {
            // A single job's own page isn't a search.
            if (path.StartsWith("/job/", StringComparison.OrdinalIgnoreCase)) return null;
            source = JobSources.Seek;
        }
        else if (TradeMeHost().IsMatch(uri.Host))
        {
            // Job searches live under /a/jobs/..., and a single listing's page has /listing/<id> in it.
            if (!path.StartsWith("/a/jobs", StringComparison.OrdinalIgnoreCase)) return null;
            if (path.Contains("/listing/", StringComparison.OrdinalIgnoreCase)) return null;
            source = JobSources.TradeMe;
        }
        else
        {
            return null;
        }

        var query = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !p.Equals("page", StringComparison.OrdinalIgnoreCase)
                     && !p.StartsWith("page=", StringComparison.OrdinalIgnoreCase));

        var builder = new UriBuilder(uri) { Fragment = "", Query = string.Join("&", query) };
        return new WatchLink(source, builder.Uri.ToString());
    }
}

/// <summary>
/// Runs the automatic watch for SEEK and Trade Me. The site decides when a scan is due and keeps all the settings.
/// The on/off switch and interval are shared, so both sites are scanned on the same schedule, one after the other.
/// The extension polls <see cref="ClaimAsync"/>, scrapes the saved search for the site it is given, and hands the jobs
/// back to <see cref="CompleteAsync"/>, which saves them, works out which are new and worth a notification, and sends
/// them to ntfy. Both sites publish to the same ntfy topic.
/// </summary>
public class WatchService(
    IDbContextFactory<WatchDbContext> watchFactory,
    IDbContextFactory<JobDbContext> jobFactory,
    NtfyClient ntfy,
    ILogger<WatchService> log)
{
    /// <summary>A scan handed to the extension is given this long to report back before it is considered lost.</summary>
    public const int ClaimMinutes = 30;

    /// <summary>Up to this many matching jobs get a notification each. More than this and they are sent as one summary.</summary>
    public const int MaxIndividual = 3;

    private const int SummaryLines = 10;

    /// <summary>The sites in the order they are scanned.</summary>
    private static readonly string[] Sites = { JobSources.Seek, JobSources.TradeMe };

    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>When the extension last asked whether a scan was due (since the site started). Shows if the extension is running.</summary>
    public DateTime? LastPollAt { get; private set; }

    /// <summary>Raised when the settings or state change. May be raised from a background thread.</summary>
    public event Action? Changed;

    private void Raise() => Changed?.Invoke();

    private static string NewTopic() =>
        "jobfinder-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();

    /// <summary>
    /// Both watch rows, SEEK first and Trade Me second, creating whichever is missing. The SEEK row also holds the settings
    /// the two sites share (on/off, interval and the ntfy topic).
    /// </summary>
    private static async Task<List<WatchSettings>> LoadAllAsync(WatchDbContext db)
    {
        var rows = await db.Watch.OrderBy(w => w.Id).ToListAsync();
        var changed = false;

        foreach (var site in Sites)
        {
            var id = WatchSettings.IdFor(site);
            if (rows.Any(r => r.Id == id)) continue;
            var row = new WatchSettings { Id = id, Source = site };
            db.Watch.Add(row);
            rows.Add(row);
            changed = true;
        }

        var main = rows.First(r => r.Id == WatchSettings.SeekId);
        if (main.Topic == "")
        {
            main.Topic = NewTopic();
            changed = true;
        }

        if (changed) await db.SaveChangesAsync();
        return rows.OrderBy(r => r.Id).ToList();
    }

    public static bool ClaimActive(WatchSettings w, DateTime nowUtc) =>
        w.ClaimId != "" && w.ClaimedAt is { } at && nowUtc < at.AddMinutes(ClaimMinutes);

    /// <summary>Whether a site's scan is due. <paramref name="main"/> is the SEEK row, which holds the shared on/off switch and interval.</summary>
    public static bool IsDue(WatchSettings w, WatchSettings main, DateTime nowUtc) =>
        main.Enabled && w.SearchUrl != "" && !ClaimActive(w, nowUtc) &&
        (w.RunRequested || w.LastAttemptAt is null || nowUtc >= w.LastAttemptAt.Value.AddMinutes(main.IntervalMinutes));

    /// <summary>When a site's next scan is due, or null if none has run yet.</summary>
    public static DateTime? NextDue(WatchSettings w, WatchSettings main) => w.LastAttemptAt?.AddMinutes(main.IntervalMinutes);

    /// <summary>The current settings and state: the SEEK row first, then the Trade Me row. Changes made to them don't write back.</summary>
    public async Task<List<WatchSettings>> GetAllAsync()
    {
        await gate.WaitAsync();
        try
        {
            await using var db = await watchFactory.CreateDbContextAsync();
            return await LoadAllAsync(db);
        }
        finally { gate.Release(); }
    }

    /// <summary>
    /// Saves the settings made on the Watch page: the shared on/off switch and interval, and each site's filters.
    /// Links, the topic and scan state are not touched.
    /// </summary>
    public async Task SaveAsync(bool enabled, int intervalMinutes, WatchSettings seekEdits, WatchSettings tradeMeEdits)
    {
        await gate.WaitAsync();
        try
        {
            await using var db = await watchFactory.CreateDbContextAsync();
            var rows = await LoadAllAsync(db);
            var seek = rows.First(r => r.Id == WatchSettings.SeekId);
            var tradeMe = rows.First(r => r.Id == WatchSettings.TradeMeId);

            seek.Enabled = enabled;
            seek.IntervalMinutes = Math.Clamp(intervalMinutes, WatchSettings.MinIntervalMinutes, WatchSettings.MaxIntervalMinutes);

            ApplyFilterEdits(seek, seekEdits);
            ApplyFilterEdits(tradeMe, tradeMeEdits);

            await db.SaveChangesAsync();
        }
        finally { gate.Release(); }
        Raise();
    }

    /// <summary>Copies the filters from a page form onto the stored row for the same site.</summary>
    private static void ApplyFilterEdits(WatchSettings row, WatchSettings edits)
    {
        // Passing the values through JobFilter drops anything the Jobs page wouldn't accept.
        var f = new JobFilter();
        f.Load(edits.Keyword, edits.Location, edits.WorkType, edits.Classification, edits.SubClass,
               edits.ListedDays, edits.Applicant, null, null, row.Source, edits.District);

        row.Keyword = f.Keyword;
        row.Location = row.Source == JobSources.TradeMe ? "" : f.Location;   // Trade Me jobs are filtered by district
        row.District = row.Source == JobSources.TradeMe ? f.District : "";
        row.WorkType = f.WorkType;
        row.Classification = f.Classification;
        row.SubClass = f.SubClass;
        row.ListedDays = f.ListedDays;
        row.Applicant = f.Applicant;   // empty for Trade Me: its jobs have no applicant strength
        row.ExcludeKeywords = (edits.ExcludeKeywords ?? "").Trim();

        var tradeMe = row.Source == JobSources.TradeMe;
        row.SkipPromoted = tradeMe && edits.SkipPromoted;
        row.QuickApplyOnly = tradeMe && edits.QuickApplyOnly;
        row.PayListedOnly = tradeMe && edits.PayListedOnly;
    }

    /// <summary>
    /// Stores a search link for the site it belongs to (a SEEK link for the SEEK watch, a Trade Me link for the Trade Me watch).
    /// Returns false if the link isn't a SEEK or Trade Me jobs search.
    /// </summary>
    public async Task<bool> SetSearchUrlAsync(string? url)
    {
        var link = WatchUrl.Parse(url);
        if (link is null) return false;

        await gate.WaitAsync();
        try
        {
            await using var db = await watchFactory.CreateDbContextAsync();
            var rows = await LoadAllAsync(db);
            var row = rows.First(r => r.Id == WatchSettings.IdFor(link.Source));
            if (row.SearchUrl != link.Url)
            {
                row.SearchUrl = link.Url;
                row.SearchUrlUpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }
        finally { gate.Release(); }
        Raise();
        return true;
    }

    /// <summary>Makes the next checks by the extension start a scan of each site that has a link, if the watch is on.</summary>
    public async Task RequestRunAsync()
    {
        await gate.WaitAsync();
        try
        {
            await using var db = await watchFactory.CreateDbContextAsync();
            var rows = await LoadAllAsync(db);
            foreach (var row in rows.Where(r => r.SearchUrl != "")) row.RunRequested = true;
            await db.SaveChangesAsync();
        }
        finally { gate.Release(); }
        Raise();
    }

    /// <summary>Replaces the ntfy topic. The phone has to subscribe to the new one.</summary>
    public async Task RegenerateTopicAsync()
    {
        await gate.WaitAsync();
        try
        {
            await using var db = await watchFactory.CreateDbContextAsync();
            var rows = await LoadAllAsync(db);
            rows.First(r => r.Id == WatchSettings.SeekId).Topic = NewTopic();
            await db.SaveChangesAsync();
        }
        finally { gate.Release(); }
        Raise();
    }

    public async Task SendTestAsync()
    {
        var rows = await GetAllAsync();
        await ntfy.SendAsync(rows[0].Topic, new NtfyMessage(
            "Job Finder test", "If you can read this, notifications are working.", null, new[] { "white_check_mark" }));
    }

    /// <summary>Called by the extension. Returns the scan to run, or null when nothing is due. SEEK is handed out before Trade Me.</summary>
    public async Task<WatchClaim?> ClaimAsync()
    {
        LastPollAt = DateTime.UtcNow;

        WatchClaim? claim = null;
        await gate.WaitAsync();
        try
        {
            await using var db = await watchFactory.CreateDbContextAsync();
            var rows = await LoadAllAsync(db);
            var main = rows[0];
            var now = DateTime.UtcNow;

            var due = rows.FirstOrDefault(w => IsDue(w, main, now));
            if (due is not null)
            {
                due.ClaimId = Guid.NewGuid().ToString("N");
                due.ClaimedAt = now;
                due.LastAttemptAt = now;
                due.RunRequested = false;
                await db.SaveChangesAsync();
                claim = new WatchClaim(due.ClaimId, due.SearchUrl, due.Source);
            }
        }
        finally { gate.Release(); }

        Raise();   // also refreshes the "extension last checked in" line
        return claim;
    }

    /// <summary>Called by the extension with the scraped jobs. Returns false if the scan is not the one currently handed out.</summary>
    public async Task<bool> CompleteAsync(WatchResultDto dto)
    {
        await gate.WaitAsync();
        try
        {
            await using var watchDb = await watchFactory.CreateDbContextAsync();
            var rows = await LoadAllAsync(watchDb);
            var w = rows.FirstOrDefault(r => r.ClaimId != "" && r.ClaimId == dto.RunId);
            if (w is null) return false;

            var main = rows[0];
            var now = DateTime.UtcNow;
            var jobs = dto.Jobs ?? new List<JobDto>();
            var result = "";
            var error = (dto.Error ?? "").Trim();

            if (jobs.Count == 0)
            {
                if (error == "") error = "The scan found no jobs.";
            }
            else
            {
                try
                {
                    result = await ProcessAsync(w, main.Topic, jobs, dto.Pages, now);
                    if (error != "") error = "The scan stopped early: " + error;
                }
                catch (HttpRequestException ex)
                {
                    log.LogWarning(ex, "Watch notification failed");
                    result = "";
                    error = $"Notification failed: {ex.Message}. The jobs from this scan were not saved, so they will be tried again on the next scan.";
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Watch scan could not be processed");
                    result = "";
                    error = $"The scan could not be saved: {ex.Message}";
                }
            }

            w.LastFinishedAt = now;
            w.LastResult = result;
            w.LastError = error;
            w.ClaimId = "";
            w.ClaimedAt = null;
            await watchDb.SaveChangesAsync();
        }
        finally { gate.Release(); }

        Raise();
        return true;
    }

    /// <summary>
    /// Saves the scanned jobs, finds the new ones that match the site's filters and notifies. It all happens in one
    /// database transaction: if ntfy can't be reached the save is undone, so those jobs still count as new next scan.
    /// </summary>
    private async Task<string> ProcessAsync(WatchSettings w, string topic, List<JobDto> jobs, int pages, DateTime now)
    {
        await using var db = await jobFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var ingest = await JobIngest.UpsertAsync(db, jobs, now);
        var matches = await MatchesAsync(db, w, ingest.AddedIds);

        var sent = "Nothing to notify.";
        if (matches.Count > 0) sent = await NotifyAsync(w, topic, matches);

        await tx.CommitAsync();

        var pageText = pages == 1 ? "1 page" : $"{pages} pages";
        return $"Scanned {pageText}, {ingest.Received} jobs: {ingest.Added} new, {matches.Count} matching your filters. {sent}";
    }

    /// <summary>
    /// Of the newly added jobs, the ones from this watch's site that pass its filters, contain none of the exclude words,
    /// and (for Trade Me) meet the Trade Me rules.
    /// </summary>
    private static async Task<List<Job>> MatchesAsync(JobDbContext db, WatchSettings w, IReadOnlyList<string> addedIds)
    {
        var found = new List<Job>();
        if (addedIds.Count == 0) return found;

        var filter = w.ToFilter();   // includes the site, so only this site's jobs can match
        foreach (var chunk in addedIds.Chunk(500))
        {
            var ids = chunk.ToList();
            var q = JobQueries.Apply(db.Jobs.AsNoTracking().Where(j => ids.Contains(j.Id)), filter, favourites: false);
            found.AddRange(await q.ToListAsync());
        }

        var terms = w.ExcludeKeywords.Split(new[] { ',', ';', '\n', '\r' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return found
            .Where(j => !IsExcluded(j, terms) && PassesSiteRules(j, w))
            .OrderByDescending(j => j.ListedDate)
            .ThenByDescending(j => j.ScrapedAt)
            .ToList();
    }

    private static bool IsExcluded(Job j, string[] terms)
    {
        if (terms.Length == 0) return false;
        var text = $"{j.Title}\n{j.Company}\n{j.Description}";
        return terms.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The extra rules only Trade Me watches have: skip promoted listings, Quick apply only, pay listed only.</summary>
    private static bool PassesSiteRules(Job j, WatchSettings w)
    {
        if (w.Source != JobSources.TradeMe) return true;
        if (w.SkipPromoted && j.IsPromoted) return false;
        if (w.QuickApplyOnly && !j.QuickApply) return false;
        if (w.PayListedOnly && string.IsNullOrWhiteSpace(j.Salary)) return false;
        return true;
    }

    private async Task<string> NotifyAsync(WatchSettings w, string topic, IReadOnlyList<Job> matches)
    {
        if (matches.Count <= MaxIndividual)
        {
            foreach (var j in matches) await ntfy.SendAsync(topic, ForJob(j));
            return matches.Count == 1 ? "Sent 1 notification." : $"Sent {matches.Count} notifications.";
        }

        await ntfy.SendAsync(topic, Summary(w, matches));
        return $"Sent one summary notification for {matches.Count} jobs.";
    }

    /// <summary>Both sites share one ntfy topic, so Trade Me notifications start with "[Trade Me]" to tell them apart.</summary>
    private static string TitlePrefix(string source) => source == JobSources.TradeMe ? "[Trade Me] " : "";

    private static NtfyMessage ForJob(Job j)
    {
        var lines = new List<string>();

        var who = string.Join(" · ", new[] { j.Company, j.Location }.Where(s => s != ""));
        if (who != "") lines.Add(who);

        var facts = string.Join(" · ", new[] { j.WorkType, j.Salary, j.ApplicantStrength }.Where(s => s != ""));
        if (facts != "") lines.Add(facts);

        if (j.Description != "") lines.Add(Truncate(j.Description, 200));

        var click = j.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? j.Url : null;
        return new NtfyMessage(
            TitlePrefix(j.Source) + (j.Title == "" ? "New job" : j.Title),
            lines.Count > 0 ? string.Join("\n", lines) : "New job listing",
            click,
            new[] { "briefcase" });
    }

    private static NtfyMessage Summary(WatchSettings w, IReadOnlyList<Job> matches)
    {
        var lines = matches.Take(SummaryLines).Select(j =>
            "• " + Truncate(
                j.Title
                + (j.Company != "" ? " - " + j.Company : "")
                + (j.Location != "" ? $" ({j.Location})" : ""),
                120));

        var text = string.Join("\n", lines);
        if (matches.Count > SummaryLines) text += $"\n+ {matches.Count - SummaryLines} more";

        // Tapping a summary opens the search so every new job is a tap away.
        return new NtfyMessage($"{TitlePrefix(w.Source)}{matches.Count} new jobs", text, w.SearchUrl, new[] { "briefcase" });
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)].TrimEnd() + "…";
}
