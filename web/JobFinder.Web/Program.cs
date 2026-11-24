using JobFinder.Web.Components;
using JobFinder.Web.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddDbContextFactory<JobDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Jobs")));

var app = builder.Build();

// Create the SQLite file and tables on first run.
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<JobDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.EnsureCreated();
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

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();