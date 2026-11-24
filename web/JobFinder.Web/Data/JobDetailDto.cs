namespace JobFinder.Web.Data;

/// <summary>Shape of a job's detail page as sent by the Chrome extension (camelCase JSON).</summary>
public record JobDetailDto(
    string? Id,
    string? Url,
    string? Title,
    string? Advertiser,
    string? AdvertiserUrl,
    string? Location,
    string? Classification,
    string? WorkType,
    string? Salary,
    string? Posted,
    string? ApplicationVolume,
    string? ApplyType,
    string? ApplyUrl,
    string? Badges,
    string? SkillsSummary,
    string? SkillsMatched,
    string? SkillsSuggested,
    string? EmployerQuestions,
    string? Emails,
    string? Phones,
    string? Description,
    string? ScrapedAt);
