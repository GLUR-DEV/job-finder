using Microsoft.EntityFrameworkCore;

namespace JobFinder.Web.Data;

public class JobDbContext(DbContextOptions<JobDbContext> options) : DbContext(options)
{
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobDetail> JobDetails => Set<JobDetail>();
    public DbSet<QueuedJob> QueuedJobs => Set<QueuedJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Job>(e =>
        {
            e.HasIndex(j => j.ListedDate);
            e.HasIndex(j => j.Location);
            e.HasIndex(j => j.WorkType);
            e.HasIndex(j => j.Classification);
        });

        modelBuilder.Entity<JobDetail>(e =>
        {
            e.HasOne(d => d.Job).WithOne(j => j.Detail)
                .HasForeignKey<JobDetail>(d => d.JobId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<QueuedJob>(e =>
        {
            e.HasOne(q => q.Job).WithMany()
                .HasForeignKey(q => q.JobId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
