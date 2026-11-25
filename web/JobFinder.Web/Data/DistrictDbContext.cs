using Microsoft.EntityFrameworkCore;

namespace JobFinder.Web.Data;

/// <summary>
/// A separate small database (districts.db) for the cached map shapes, so adding the map
/// doesn't change the jobs database.
/// </summary>
public class DistrictDbContext(DbContextOptions<DistrictDbContext> options) : DbContext(options)
{
    public DbSet<District> Districts => Set<District>();
}
