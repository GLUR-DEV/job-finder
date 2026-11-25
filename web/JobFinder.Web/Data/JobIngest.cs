using Microsoft.EntityFrameworkCore;

namespace JobFinder.Web.Data;

/// <summary>What happened when a batch of scraped jobs was saved.</summary>
public record IngestResult(int Received, int Added, int Updated, int Skipped, IReadOnlyList<string> AddedIds);

/// <summary>Saves jobs sent by the extension. Shared by the plain upload and the automatic watch.</summary>
public static class JobIngest
{
    /// <summary>Adds new jobs and updates existing ones by id. If the same id appears twice, the last one wins.</summary>
    public static async Task<IngestResult> UpsertAsync(JobDbContext db, IReadOnlyCollection<JobDto> incoming, DateTime nowUtc)
    {
        var valid = incoming
            .Where(j => !string.IsNullOrWhiteSpace(j.Id))
            .GroupBy(j => j.Id!.Trim())
            .Select(g => (Id: g.Key, Dto: g.Last()))
            .ToList();

        int added = 0, updated = 0;
        var addedIds = new List<string>();

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
                    addedIds.Add(id);
                }
                JobMapper.Apply(job, dto, nowUtc);
            }
            await db.SaveChangesAsync();
        }

        return new IngestResult(incoming.Count, added, updated, incoming.Count - valid.Count, addedIds);
    }
}
