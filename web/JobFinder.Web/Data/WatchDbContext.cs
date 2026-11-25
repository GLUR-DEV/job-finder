using Microsoft.EntityFrameworkCore;

namespace JobFinder.Web.Data;

/// <summary>
/// A separate small database (watch.db) for the automatic watch, so adding it
/// doesn't change the jobs database.
/// </summary>
public class WatchDbContext(DbContextOptions<WatchDbContext> options) : DbContext(options)
{
    public DbSet<WatchSettings> Watch => Set<WatchSettings>();
}
