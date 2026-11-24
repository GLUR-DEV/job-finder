using System.ComponentModel.DataAnnotations;

namespace JobFinder.Web.Data;

/// <summary>A job waiting for the extension to scrape its detail page.</summary>
public class QueuedJob
{
    [Key]
    public string JobId { get; set; } = "";
    public Job? Job { get; set; }

    /// <summary>Also used to keep the order jobs were queued in.</summary>
    public DateTime QueuedAt { get; set; }
}
