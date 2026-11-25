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
    c.DefaultRequestHeaders.UserAgent.ParseAdd("JobFinder/1.4.1 (personal job search tool; https://github.com/GLUR-DEV/job-finder)");
    c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
});
builder.Services.AddSingleton<SuburbBoundaries>();
builder.Services.AddSingleton<DistrictLookupService>();

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

    // Replace any circles or "not found" results from earlier runs with official Auckland suburb outlines.
    scope.ServiceProvider.GetRequiredService<DistrictLookupService>().ApplyOfficialBoundaries();
}

app.UseAntiforgery();
app.MapStaticAssets();

// API used by the Chrome extension. Accepts a JSON array of jobs and upserts them by id.
app.MapPost("/api/jobs", async (List<JobDto> incoming, IDbContextFactory<JobDbContext> factory) =>
{
    var now = DateTime.UtcNow;

    // If the same id appears twice in one request, the last one wins.
    var valid = incoming
        .Where(j => !string.IsNullOrWhiteSpace(j.Id))
        .GroupBy(j => j.Id!.Trim())
        .Select(g => (Id: g.Key, Dto: g.Last()))
        .ToList();

    await using var db = await factory.CreateDbContextAsync();
    int added = 0, updated = 0;

    foreach (var chunk in valid.Chunk(200))
    {
        var ids = chunk.Select(c => c.Id).ToList();
        var existing = await db.Jobs.Where(j => ids.Contains(j.Id)).ToDictionaryAsync(j => j.Id);

        foreach (var (id, dto) in chunk)
        {
            if (existing.TryGetValue(id, out var job))
            {
                updated++;
            }
            else
            {
                job = new Job { Id = id };
                db.Jobs.Add(job);
                added++;
            }
            JobMapper.Apply(job, dto, now);
        }
        await db.SaveChangesAsync();
    }

    return Results.Ok(new { received = incoming.Count, added, updated, skipped = incoming.Count - valid.Count });
}).DisableAntiforgery();

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
