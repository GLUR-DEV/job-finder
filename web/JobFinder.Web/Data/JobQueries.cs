using Microsoft.EntityFrameworkCore;

namespace JobFinder.Web.Data;

/// <summary>The one place that turns a <see cref="JobFilter"/> into a database query, so every page filters the same way.</summary>
public static class JobQueries
{
    /// <summary>
    /// Applies the filter. With <paramref name="favourites"/> set, only jobs with scraped details are included,
    /// the keyword also searches the job page text, and the Contact filter applies.
    /// Work hours can't be checked in the database, so use <see cref="HoursActive"/> and <see cref="HoursMatch"/> for that.
    /// </summary>
    public static IQueryable<Job> Apply(IQueryable<Job> q, JobFilter f, bool favourites)
    {
        if (favourites) q = q.Where(j => j.Detail != null);

        var k = f.Keyword.Trim();
        if (k != "")
        {
            var p = $"%{k}%";
            if (!favourites)
            {
                q = q.Where(j => EF.Functions.Like(j.Title, p)
                              || EF.Functions.Like(j.Company, p)
                              || EF.Functions.Like(j.Description, p));
            }
            else
            {
                q = q.Where(j => EF.Functions.Like(j.Title, p)
                              || EF.Functions.Like(j.Company, p)
                              || EF.Functions.Like(j.Description, p)
                              || EF.Functions.Like(j.Detail!.Title, p)
                              || EF.Functions.Like(j.Detail!.Advertiser, p)
                              || EF.Functions.Like(j.Detail!.Description, p)
                              || EF.Functions.Like(j.Detail!.SkillsMatched, p)
                              || EF.Functions.Like(j.Detail!.SkillsSuggested, p)
                              || EF.Functions.Like(j.Detail!.EmployerQuestions, p)
                              || EF.Functions.Like(j.Detail!.Emails, p)
                              || EF.Functions.Like(j.Detail!.Phones, p));
            }
        }

        var src = f.Source; var loc = f.Location; var wt = f.WorkType; var cls = f.Classification; var sub = f.SubClass;
        if (src != "") q = q.Where(j => j.Source == src);
        if (loc != "") q = q.Where(j => j.Location == loc);
        if (wt != "") q = q.Where(j => j.WorkType == wt);
        if (cls != "") q = q.Where(j => j.Classification == cls);
        if (sub != "") q = q.Where(j => j.SubClass == sub);

        // Trade Me jobs have no applicant strength, so the filter is ignored when only Trade Me is shown.
        if (f.ApplicantApplies)
        {
            switch (f.Applicant)
            {
                case "strong": q = q.Where(j => j.ApplicantStrength == ApplicantStrengths.Strong); break;
                case "very":   q = q.Where(j => j.ApplicantStrength == ApplicantStrengths.VeryStrong); break;
                case "both":   q = q.Where(j => j.ApplicantStrength != ""); break;
            }
        }

        if (f.ListedDays > 0)
        {
            var cutoff = DateTime.UtcNow.AddDays(-f.ListedDays);
            q = q.Where(j => j.ListedDate != null && j.ListedDate >= cutoff);
        }

        if (favourites)
        {
            switch (f.Contact)
            {
                case "phone": q = q.Where(j => j.Detail!.Phones != "" && j.Detail!.Emails == ""); break;
                case "email": q = q.Where(j => j.Detail!.Emails != "" && j.Detail!.Phones == ""); break;
                case "both":  q = q.Where(j => j.Detail!.Emails != "" && j.Detail!.Phones != ""); break;
                case "none":  q = q.Where(j => j.Detail!.Emails == "" && j.Detail!.Phones == ""); break;
            }
        }

        return q;
    }

    /// <summary>True when the Work hours filter is on and so needs checking in memory after the query.</summary>
    public static bool HoursActive(JobFilter f, bool favourites) => favourites && f.Hours != "";

    /// <summary>Whether a job ad passes the Work hours filter.</summary>
    public static bool HoursMatch(string? description, JobFilter f) =>
        HoursDetector.Has(description) == (f.Hours == "yes");
}
