using Microsoft.EntityFrameworkCore;

namespace JobFinder.Web.Data;

public class JobDbContext(DbContextOptions<JobDbContext> options) : DbContext(options)
{
    public DbSet<Job> Jobs => Set<Job>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Job>(e =>
        {
            e.HasIndex(j => j.ListedDate);
            e.HasIndex(j => j.Location);
            e.HasIndex(j => j.WorkType);
            e.HasIndex(j => j.Classification);
        });
    }
}