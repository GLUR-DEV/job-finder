using System.Text.RegularExpressions;

namespace JobFinder.Web.Data;

/// <summary>
/// The cover letter prompt. Anything in {{double_braces}} is filled in for the job being written for.
/// Empty fields become "(not provided)". Unknown placeholders are left as typed.
/// </summary>
public static partial class CoverLetterPrompt
{
    public const string NotProvided = "(not provided)";

    /// <summary>Every placeholder the prompt can use, with what it holds, for the help text on the Documents page.</summary>
    public static readonly (string Name, string Meaning)[] Placeholders =
    {
        ("job_title", "Job title"),
        ("company", "Company or advertiser"),
        ("location", "Location (and district on Trade Me)"),
        ("work_type", "Work type"),
        ("classification", "Classification"),
        ("pay", "Pay or salary line"),
        ("reference_number", "Reference number (Trade Me)"),
        ("contact_person", "Contact person (Trade Me)"),
        ("description", "The full job ad"),
        ("skills", "Skills listed on the job page (SEEK)"),
        ("employer_questions", "Employer questions (SEEK)"),
        ("company_benefits", "Company benefits (Trade Me)"),
        ("application_details", "How to apply (Trade Me)"),
        ("cv", "Your CV text from this page"),
    };

    public const string Default = """
You are helping a job seeker in New Zealand write a cover letter for one specific job.
Write it in New Zealand English, in the first person, as the applicant.

RULES
- Use ONLY facts that appear in the CV below. Never invent employers, dates,
  qualifications, numbers, licences or skills. If the job asks for something the
  CV doesn't show, either leave it out or briefly show willingness to learn —
  never claim it.
- Pick the 2–3 parts of the CV that best match what this job actually asks for,
  and connect each one to a specific requirement or duty in the job ad.
- If the ad lists employer questions or application instructions, address
  them where it is natural to do so.
- Address it to {{contact_person}} if a name is given, otherwise "Kia ora" or
  "Dear Hiring Manager". Mention the job title, and the reference number if given.
- 250–350 words, 3–4 short paragraphs, then a sign-off with the applicant's
  name from the CV.
- Sound like a real person: plain, specific, confident, a little warm. Match the
  tone to the job (casual for hospitality/retail, more formal for office roles).
- Do NOT use these: "I am writing to express my interest", "I am excited to apply",
  "passionate", "dynamic", "fast-paced environment", "leverage", "synergy",
  "proven track record", "I believe I would be a great fit", "delve", "thrilled",
  em dashes, or bullet points.
- Output only the letter text. No subject line, no notes, no placeholders like [Your Name].

THE JOB
Title: {{job_title}}
Company / advertiser: {{company}}
Location: {{location}}
Work type: {{work_type}}
Classification: {{classification}}
Pay: {{pay}}
Reference number: {{reference_number}}
Contact person: {{contact_person}}

Job description:
{{description}}

Skills the employer lists: {{skills}}
Employer questions: {{employer_questions}}
Benefits mentioned: {{company_benefits}}
How to apply: {{application_details}}

THE APPLICANT'S CV
{{cv}}
""";

    /// <summary>The values for one job. The job page's details win over the search card's where both exist.</summary>
    public static Dictionary<string, string> ValuesFor(Job job, string cvText)
    {
        var d = job.Detail ?? new JobDetail();

        var location = First(d.Location, job.Location);
        if (job.District != "" && !location.Contains(job.District, StringComparison.OrdinalIgnoreCase))
            location = location == "" ? job.District : $"{location}, {job.District}";

        var skills = string.Join("; ", new[] { d.SkillsMatched, d.SkillsSuggested }.Where(s => s.Trim() != ""));

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["job_title"] = First(d.Title, job.Title),
            ["company"] = First(d.Advertiser, job.Company),
            ["location"] = location,
            ["work_type"] = First(d.WorkType, job.WorkType),
            ["classification"] = First(d.Classification, job.Classification),
            ["pay"] = First(d.Salary, job.Salary),
            ["reference_number"] = d.ReferenceNumber,
            ["contact_person"] = d.ContactPerson,
            ["description"] = First(d.Description, job.Description),
            ["skills"] = skills,
            ["employer_questions"] = d.EmployerQuestions,
            ["company_benefits"] = d.CompanyBenefits,
            ["application_details"] = d.ApplicationDetails,
            ["cv"] = cvText,
        };
    }

    /// <summary>Fills every known {{placeholder}} in the template.</summary>
    public static string Build(string template, IReadOnlyDictionary<string, string> values) =>
        PlaceholderRegex().Replace(template, m =>
            values.TryGetValue(m.Groups[1].Value, out var v)
                ? (string.IsNullOrWhiteSpace(v) ? NotProvided : v.Trim())
                : m.Value);

    private static string First(params string[] options) =>
        options.FirstOrDefault(o => !string.IsNullOrWhiteSpace(o))?.Trim() ?? "";

    [GeneratedRegex(@"\{\{\s*([A-Za-z_]+)\s*\}\}")]
    private static partial Regex PlaceholderRegex();
}
