namespace JobFinder.Web.Data;

/// <summary>Shape of a job as sent by the Chrome extension (camelCase JSON).</summary>
public record JobDto(
    string? Id,
    string? Title,
    string? Company,
    string? Location,
    string? Salary,
    string? WorkType,
    string? Classification,
    string? SubClass,
    string? Listed,
    string? Description,
    string? Url,
    string? ScrapedAt);