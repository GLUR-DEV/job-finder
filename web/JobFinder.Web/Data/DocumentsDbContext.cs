using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace JobFinder.Web.Data;

/// <summary>
/// A separate small database (documents.db) for the CV, the Gemini settings and saved cover letters,
/// so adding cover letters doesn't change the jobs database.
/// </summary>
public class DocumentsDbContext(DbContextOptions<DocumentsDbContext> options) : DbContext(options)
{
    public DbSet<DocumentSettings> Settings => Set<DocumentSettings>();
    public DbSet<CoverLetter> CoverLetters => Set<CoverLetter>();

    /// <summary>The single settings row, created on first use.</summary>
    public async Task<DocumentSettings> GetSettingsAsync()
    {
        var s = await Settings.FindAsync(DocumentSettings.SingleId);
        if (s is null)
        {
            s = new DocumentSettings();
            Settings.Add(s);
            await SaveChangesAsync();
        }
        return s;
    }
}

/// <summary>The Documents page: the CV as plain text, the Gemini key and model, and the cover letter prompt. Always one row.</summary>
public class DocumentSettings
{
    public const int SingleId = 1;
    public const string DefaultModel = "gemini-3.5-flash-lite";

    [Key]
    public int Id { get; set; } = SingleId;

    /// <summary>The CV as plain text. This is what is sent to Gemini, so it can be checked and edited on the Documents page.</summary>
    public string CvText { get; set; } = "";

    /// <summary>Name of the last uploaded CV file, for display only.</summary>
    public string CvFileName { get; set; } = "";
    public DateTime? CvUpdatedAt { get; set; }

    public string GeminiApiKey { get; set; } = "";
    public string Model { get; set; } = DefaultModel;

    /// <summary>The cover letter prompt with {{placeholders}}. Empty means the default prompt.</summary>
    public string Prompt { get; set; } = "";

    public string EffectivePrompt => string.IsNullOrWhiteSpace(Prompt) ? CoverLetterPrompt.Default : Prompt;
    public string EffectiveModel => string.IsNullOrWhiteSpace(Model) ? DefaultModel : Model.Trim();
}

/// <summary>A cover letter saved against one job (by the job's id in jobs.db).</summary>
public class CoverLetter
{
    [Key]
    public string JobId { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime GeneratedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
