using JobFinder.Web.Components;
using JobFinder.Web.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddDbContextFactory<JobDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Jobs")));

// The Map page keeps its cached district outlines in a separate database (districts.db).
builder.Services.AddDbContextFactory<DistrictDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Districts") ?? "Data Source=districts.db"));

// Nominatim (OpenStreetMap search) asks for an identifying User-Agent and at most one request per second.
builder.Services.AddHttpClient("nominatim", c =>
{
    c.BaseAddress = new Uri("https://nominatim.openstreetmap.org/");
    c.DefaultRequestHeaders.UserAgent.ParseAdd("JobFinder/1.6.5 (personal job search tool; https://github.com/GLUR-DEV/job-finder)");
    c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
});
builder.Services.AddSingleton<SuburbBoundaries>();
builder.Services.AddSingleton<TradeMeBoundaries>();
builder.Services.AddSingleton<DistrictLookupService>();

// The automatic watch keeps its settings in its own database (watch.db) and notifies through the public ntfy.sh server.
builder.Services.AddDbContextFactory<WatchDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Watch") ?? "Data Source=watch.db"));
builder.Services.AddHttpClient("ntfy", c =>
{
    c.BaseAddress = new Uri(NtfyClient.Server + "/");
    c.DefaultRequestHeaders.UserAgent.ParseAdd("JobFinder (personal job search tool; https://github.com/GLUR-DEV/job-finder)");
});
builder.Services.AddSingleton<NtfyClient>();
builder.Services.AddSingleton<WatchService>();

var app = builder.Build();

// Create the SQLite file and tables on first run.
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<JobDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.EnsureCreated();

    var districtFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<DistrictDbContext>>();
    using var districtDb = districtFactory.CreateDbContext();
    districtDb.Database.EnsureCreated();

    var watchFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<WatchDbContext>>();
    using var watchDb = watchFactory.CreateDbContext();
    watchDb.Database.EnsureCreated();
    WatchSchema.Upgrade(watchDb);   // adds columns that came after the first watch.db, keeping existing settings

    // Replace any circles or "not found" results from earlier runs with official Auckland suburb outlines.
    scope.ServiceProvider.GetRequiredService<DistrictLookupService>().ApplyOfficialBoundaries();
}

app.UseAntiforgery();
app.MapStaticAssets();

// API used by the Chrome extension. Accepts a JSON array of jobs and upserts them by id.
app.MapPost("/api/jobs", async (List<JobDto> incoming, IDbContextFactory<JobDbContext> factory) =>
{
    await using var db = await factory.CreateDbContextAsync();
    var r = await JobIngest.UpsertAsync(db, incoming, DateTime.UtcNow);
    return Results.Ok(new { received = r.Received, added = r.Added, updated = r.Updated, skipped = r.Skipped });
}).DisableAntiforgery();

// The automatic watch. The extension scrape buttons tell the site which SEEK search to watch,
// and the extension then polls /claim to find out when a scan is due.
app.MapPost("/api/watch/url", async (WatchUrlDto dto, WatchService watch) =>
    await watch.SetSearchUrlAsync(dto.Url)
        ? Results.Ok(new { saved = true })
        : Results.BadRequest(new { error = "Not a SEEK search link." })).DisableAntiforgery();

app.MapPost("/api/watch/claim", async (WatchService watch) =>
{
    var claim = await watch.ClaimAsync();
    return Results.Ok(claim is null
        ? new { run = false, runId = "", url = "", source = "" }
        : new { run = true, runId = claim.RunId, url = claim.Url, source = claim.Source });
}).DisableAntiforgery();

app.MapPost("/api/watch/results", async (WatchResultDto dto, WatchService watch) =>
    await watch.CompleteAsync(dto)
        ? Results.Ok(new { saved = true })
        : Results.Conflict(new { error = "That scan is no longer the current one." })).DisableAntiforgery();

// Jobs waiting for the extension to scrape their detail page.
app.MapGet("/api/queue", async (IDbContextFactory<JobDbContext> factory) =>
{
    await using var db = await factory.CreateDbContextAsync();
    var items = await db.QueuedJobs
        .OrderBy(q => q.QueuedAt)
        .Select(q => new { id = q.JobId, url = q.Job!.Url })
        .ToListAsync();
    return Results.Ok(items);
});

// Receives one job's detail page from the extension and takes the job off the queue.
app.MapPost("/api/job-details", async (JobDetailDto dto, IDbContextFactory<JobDbContext> factory) =>
{
    var id = dto.Id?.Trim();
    if (string.IsNullOrEmpty(id))
        return Results.BadRequest(new { error = "Missing job id." });

    await using var db = await factory.CreateDbContextAsync();
    if (!await db.Jobs.AnyAsync(j => j.Id == id))
        return Results.NotFound(new { error = "Unknown job id. Send the job to the site first." });

    var detail = await db.JobDetails.FindAsync(id);
    if (detail is null)
    {
        detail = new JobDetail { JobId = id };
        db.JobDetails.Add(detail);
    }
    JobDetailMapper.Apply(detail, dto, DateTime.UtcNow);

    var queued = await db.QueuedJobs.FindAsync(id);
    if (queued is not null) db.QueuedJobs.Remove(queued);

    await db.SaveChangesAsync();
    return Results.Ok(new { id, saved = true });
}).DisableAntiforgery();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
