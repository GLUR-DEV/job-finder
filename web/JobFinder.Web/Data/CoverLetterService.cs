using System.Text;
using Microsoft.EntityFrameworkCore;

namespace JobFinder.Web.Data;

/// <summary>Reads CVs into text, writes cover letters with Gemini and saves them against jobs.</summary>
public class CoverLetterService(IDbContextFactory<DocumentsDbContext> docsFactory, GeminiClient gemini)
{
    public const long MaxCvBytes = 10 * 1024 * 1024;
    public const double Temperature = 0.6;

    /// <summary>The text of a CV file: .pdf (read by Gemini), .docx, or .txt/.md.</summary>
    public async Task<string> ReadCvAsync(string fileName, byte[] bytes, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        switch (ext)
        {
            case ".docx":
                using (var ms = new MemoryStream(bytes)) return WordDocs.ReadText(ms);

            case ".txt":
            case ".md":
                return WordDocs.Tidy(Encoding.UTF8.GetString(bytes));

            case ".pdf":
                await using (var db = await docsFactory.CreateDbContextAsync(ct))
                {
                    var s = await db.GetSettingsAsync();
                    if (string.IsNullOrWhiteSpace(s.GeminiApiKey))
                        throw new InvalidOperationException("Save your Gemini API key first. PDFs are read by Gemini.");
                    var text = await gemini.ExtractPdfTextAsync(s.GeminiApiKey, s.EffectiveModel, bytes, ct);
                    return WordDocs.Tidy(text);
                }

            case ".doc":
                throw new InvalidOperationException("Old .doc files aren't supported. Save it as .docx or PDF in Word, or paste the text.");

            default:
                throw new InvalidOperationException("Upload a .pdf, .docx or .txt file, or paste the text.");
        }
    }

    /// <summary>Writes a cover letter for the job (which must include its Detail) and saves it, replacing any earlier one.</summary>
    public async Task<CoverLetter> GenerateAsync(Job job, CancellationToken ct = default)
    {
        await using var db = await docsFactory.CreateDbContextAsync(ct);
        var s = await db.GetSettingsAsync();

        if (string.IsNullOrWhiteSpace(s.GeminiApiKey))
            throw new InvalidOperationException("Add your Gemini API key on the Documents page first.");
        if (string.IsNullOrWhiteSpace(s.CvText))
            throw new InvalidOperationException("Upload or paste your CV on the Documents page first.");

        var prompt = CoverLetterPrompt.Build(s.EffectivePrompt, CoverLetterPrompt.ValuesFor(job, s.CvText));
        var text = Clean(await gemini.GenerateAsync(s.GeminiApiKey, s.EffectiveModel, prompt, Temperature, ct));

        var now = DateTime.UtcNow;
        var letter = await db.CoverLetters.FindAsync(new object[] { job.Id }, ct);
        if (letter is null)
        {
            letter = new CoverLetter { JobId = job.Id };
            db.CoverLetters.Add(letter);
        }
        letter.Text = text;
        letter.GeneratedAt = now;
        letter.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return letter;
    }

    public async Task<CoverLetter?> GetAsync(string jobId)
    {
        await using var db = await docsFactory.CreateDbContextAsync();
        return await db.CoverLetters.AsNoTracking().FirstOrDefaultAsync(c => c.JobId == jobId);
    }

    public async Task<HashSet<string>> JobIdsWithLettersAsync(IEnumerable<string> jobIds)
    {
        var ids = jobIds.ToList();
        await using var db = await docsFactory.CreateDbContextAsync();
        return (await db.CoverLetters.Where(c => ids.Contains(c.JobId)).Select(c => c.JobId).ToListAsync()).ToHashSet();
    }

    public async Task SaveAsync(string jobId, string text)
    {
        await using var db = await docsFactory.CreateDbContextAsync();
        var letter = await db.CoverLetters.FindAsync(jobId);
        var now = DateTime.UtcNow;
        if (letter is null)
        {
            letter = new CoverLetter { JobId = jobId, GeneratedAt = now };
            db.CoverLetters.Add(letter);
        }
        letter.Text = text.Trim();
        letter.UpdatedAt = now;
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(string jobId)
    {
        await using var db = await docsFactory.CreateDbContextAsync();
        var letter = await db.CoverLetters.FindAsync(jobId);
        if (letter is null) return;
        db.CoverLetters.Remove(letter);
        await db.SaveChangesAsync();
    }

    /// <summary>A file name like "Cover letter - Motel Cleaner - Acme Ltd.docx", with characters Windows doesn't allow removed.</summary>
    public static string FileName(Job? job)
    {
        var parts = new List<string> { "Cover letter" };
        if (job is not null)
        {
            var title = job.Detail?.Title is { Length: > 0 } t ? t : job.Title;
            var company = job.Detail?.Advertiser is { Length: > 0 } a ? a : job.Company;
            if (title != "") parts.Add(title);
            if (company != "") parts.Add(company);
        }
        var name = string.Join(" - ", parts);
        var bad = Path.GetInvalidFileNameChars().Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }).ToHashSet();
        name = new string(name.Where(c => !bad.Contains(c) && !char.IsControl(c)).ToArray()).Trim();
        if (name.Length > 120) name = name[..120].Trim();
        return name + ".docx";
    }

    /// <summary>Removes markdown that sometimes slips in (bold markers, a "Subject:" line or code fences).</summary>
    private static string Clean(string text)
    {
        var t = text.Replace("```", "").Replace("**", "").Trim();
        var lines = t.Split('\n').ToList();
        if (lines.Count > 0 && lines[0].TrimStart().StartsWith("Subject:", StringComparison.OrdinalIgnoreCase))
            lines.RemoveAt(0);
        return WordDocs.Tidy(string.Join('\n', lines));
    }
}
