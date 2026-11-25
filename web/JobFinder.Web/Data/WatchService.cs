using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace JobFinder.Web.Data;

/// <summary>Body of POST /api/watch/url.</summary>
public record WatchUrlDto(string? Url);

/// <summary>Body of POST /api/watch/results: everything the extension scraped for one scan.</summary>
public record WatchResultDto(string? RunId, List<JobDto>? Jobs, int Pages, string? Error);

/// <summary>What the extension is told to do when a scan is due.</summary>
public record WatchClaim(string RunId, string Url);

/// <summary>Checks and tidies the SEEK link the watch rescans.</summary>
public static partial class WatchUrl
{
    [GeneratedRegex(@"^([^.]+\.)*seek\.(com(\.au)?|co\.nz)$", RegexOptions.IgnoreCase)]
    private static partial Regex SeekHost();

    /// <summary>
    /// Returns the link without its page number (so scans always start at page 1) and fragment,
    /// or null if it isn't an https SEEK search link.
    /// </summary>
    public static string? Normalise(string? raw)
    {
        if (!Uri.TryCreate(raw?.Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps || !SeekHost().IsMatch(uri.Host)) return null;

        // A single job's own page isn't a search.
        if (uri.AbsolutePath.StartsWith("/job/", StringComparison.OrdinalIgnoreCase)) return null;

        var query = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !p.Equals("page", StringComparison.OrdinalIgnoreCase)
                     && !p.StartsWith("page=", StringComparison.OrdinalIgnoreCase));

        var builder = new UriBuilder(uri) { Fragment = "", Query = string.Join("&", query) };
        return builder.Uri.ToString();
    }
}

/// <summary>
/// Runs the automatic watch. The site decides when a scan is due and keeps all the settings. The extension
/// polls <see cref="ClaimAsync"/>, scrapes the saved SEEK search, and hands the jobs back to
/// <see cref="CompleteAsync"/>, which saves them, works out which are new and worth a notification,
/// and sends them to ntfy.
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

    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>When the extension last asked whether a scan was due (since the site started). Shows if the extension is running.</summary>
    public DateTime? LastPollAt { get; private set; }

    /// <summary>Raised when the settings or state change. May be raised from a background thread.</summary>
    public event Action? Changed;

    private void Raise() => Changed?.Invoke();

    private static string NewTopic() =>
        "jobfinder-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();

    private static async Task<WatchSettings> LoadAsync(WatchDbContext db)
    {
        var w = await db.Watch.FirstOrDefaultAsync();
        if (w is null)
        {
            w = new WatchSettings { Topic = NewTopic() };
            db.Watch.Add(w);
            await db.SaveChangesAsync();
        }
        return w;
    }

    public static bool ClaimActive(WatchSettings w, DateTime nowUtc) =>
        w.ClaimId != "" && w.ClaimedAt is { } at && nowUtc < at.AddMinutes(ClaimMinutes);

    public static bool IsDue(WatchSettings w, DateTime nowUtc) =>
        w.Enabled && w.SearchUrl != "" && !ClaimActive(w, nowUtc) &&
        (w.RunRequested || w.LastAttemptAt is null || nowUtc >= w.LastAttemptAt.Value.AddMinutes(w.IntervalMinutes));

    /// <summary>When the next scan is due, or null if none has run yet.</summary>
    public static DateTime? NextDue(WatchSettings w) => w.LastAttemptAt?.AddMinutes(w.IntervalMinutes);

    /// <summary>The current settings and state. Safe to read from anywhere; changes don't write back.</summary>
    public async Task<WatchSettings> GetAsync()
    {
        await gate.WaitAsync();
        try
        {
            await using var db = await watchFactory.CreateDbContextAsync();
            return await LoadAsync(db);
        }
        finally { gate.Release(); }
    }

    /// <summary>Saves the settings made on the Watch page. Link, topic and scan state are not touched.</summary>
    public async Task SaveAsync(WatchSettings edited)
    {
        await gate.WaitAsync();
        try
        {
            await using var db = await watchFactory.CreateDbContextAsync();
            var w = await LoadAsync(db);

            w.Enabled = edited.Enabled;
            w.IntervalMinutes = Math.Clamp(edited.IntervalMinutes, WatchSettings.MinIntervalMinutes, WatchSettings.MaxIntervalMinutes);

            // Passing the values through JobFilter drops anything the Jobs page wouldn't accept.
            var f = edited.ToFilter();
            w.Keyword = f.Keyword;
            w.Location = f.Location;
            w.WorkType = f.WorkType;
            w.Classification = f.Classification;
            w.SubClass = f.SubClass;
            w.ListedDays = f.ListedDays;
            w.Applicant = f.Applicant;
            w.ExcludeKeywords = (edited.ExcludeKeywords ?? "").Trim();

            await db.SaveChangesAsync();
        }
        finally { gate.Release(); }
        Raise();
    }

    /// <summary>Stores the SEEK search the watch should rescan. Returns false if the link isn't a SEEK search.</summary>
    public async Task<bool> SetSearchUrlAsync(string? url)
    {
        var clean = WatchUrl.Normalise(url);
        if (clean is null) return false;

        await gate.WaitAsync();
        try
        {
            await using var db = await watchFactory.CreateDbContextAsync();
            var w = await LoadAsync(db);
            if (w.SearchUrl != clean)
            {
                w.SearchUrl = clean;
                w.SearchUrlUpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }
        finally { gate.Release(); }
        Raise();
        return true;
    }

    /// <summary>Makes the next check by the extension start a scan, if the watch is on and has a link.</summary>
    public async Task RequestRunAsync()
    {
        await gate.WaitAsync();
        try
        {
            await using var db = await watchFactory.CreateDbContextAsync();
            var w = await LoadAsync(db);
            if (w.SearchUrl == "") return;
            w.RunRequested = true;
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
            var w = await LoadAsync(db);
            w.Topic = NewTopic();
            await db.SaveChangesAsync();
        }
        finally { gate.Release(); }
        Raise();
    }

    public async Task SendTestAsync()
    {
        var w = await GetAsync();
        await ntfy.SendAsync(w.Topic, new NtfyMessage(
            "Job Finder test", "If you can read this, notifications are working.", null, new[] { "white_check_mark" }));
    }

    /// <summary>Called by the extension. Returns the scan to run, or null when nothing is due.</summary>
    public async Task<WatchClaim?> ClaimAsync()
    {
        LastPollAt = DateTime.UtcNow;

        WatchClaim? claim = null;
        await gate.WaitAsync();
        try
        {
            await using var db = await watchFactory.CreateDbContextAsync();
            var w = await LoadAsync(db);
            var now = DateTime.UtcNow;

            if (IsDue(w, now))
            {
                w.ClaimId = Guid.NewGuid().ToString("N");
                w.ClaimedAt = now;
                w.LastAttemptAt = now;
                w.RunRequested = false;
                await db.SaveChangesAsync();
                claim = new WatchClaim(w.ClaimId, w.SearchUrl);
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
            var w = await LoadAsync(watchDb);
            if (w.ClaimId == "" || w.ClaimId != dto.RunId) return false;

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
                    result = await ProcessAsync(w, jobs, dto.Pages, now);
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
    /// Saves the scanned jobs, finds the new ones that match the watch's filters and notifies. It all happens in one
    /// database transaction: if ntfy can't be reached the save is undone, so those jobs still count as new next scan.
    /// </summary>
    private async Task<string> ProcessAsync(WatchSettings w, List<JobDto> jobs, int pages, DateTime now)
    {
        await using var db = await jobFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var ingest = await JobIngest.UpsertAsync(db, jobs, now);
        var matches = await MatchesAsync(db, w, ingest.AddedIds);

        var sent = "Nothing to notify.";
        if (matches.Count > 0) sent = await NotifyAsync(w, matches);

        await tx.CommitAsync();

        var pageText = pages == 1 ? "1 page" : $"{pages} pages";
        return $"Scanned {pageText}, {ingest.Received} jobs: {ingest.Added} new, {matches.Count} matching your filters. {sent}";
    }

    /// <summary>Of the newly added jobs, the ones that pass the watch's filters and contain none of the exclude words.</summary>
    private static async Task<List<Job>> MatchesAsync(JobDbContext db, WatchSettings w, IReadOnlyList<string> addedIds)
    {
        var found = new List<Job>();
        if (addedIds.Count == 0) return found;

        var filter = w.ToFilter();
        foreach (var chunk in addedIds.Chunk(500))
        {
            var ids = chunk.ToList();
            var q = JobQueries.Apply(db.Jobs.AsNoTracking().Where(j => ids.Contains(j.Id)), filter, favourites: false);
            found.AddRange(await q.ToListAsync());
        }

        var terms = w.ExcludeKeywords.Split(new[] { ',', ';', '\n', '\r' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return found
            .Where(j => !IsExcluded(j, terms))
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

    private async Task<string> NotifyAsync(WatchSettings w, IReadOnlyList<Job> matches)
    {
        if (matches.Count <= MaxIndividual)
        {
            foreach (var j in matches) await ntfy.SendAsync(w.Topic, ForJob(j));
            return matches.Count == 1 ? "Sent 1 notification." : $"Sent {matches.Count} notifications.";
        }

        await ntfy.SendAsync(w.Topic, Summary(w, matches));
        return $"Sent one summary notification for {matches.Count} jobs.";
    }

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
            j.Title == "" ? "New job" : j.Title,
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

        // Tapping a summary opens the SEEK search so every new job is a tap away.
        return new NtfyMessage($"{matches.Count} new jobs", text, w.SearchUrl, new[] { "briefcase" });
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)].TrimEnd() + "…";
}
