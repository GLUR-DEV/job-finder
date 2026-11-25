namespace JobFinder.Web.Data;

/// <summary>
/// Shape of a job as sent by the Chrome extension (camelCase JSON).
/// The fields after <c>ScrapedAt</c> are optional: SEEK jobs leave them out, Trade Me jobs fill them in.
/// </summary>
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
    string? ListingStatus,
    string? ApplicantStrength,
    string? Badges,
    string? AdType,
    string? Section,
    string? Description,
    string? Url,
    string? ScrapedAt,
    string? Source = null,
    bool? IsPromoted = null,
    bool? QuickApply = null,
    string? ListedAt = null,
    string? Pay = null,
    string? PayInText = null,
    string? Tags = null,
    string? BulletPoints = null,
    string? Category = null,
    string? SubCategory = null,
    string? SubSubCategory = null,
    string? Region = null,
    string? District = null,
    string? UrlPath = null,
    string? ImageUrl = null,
    string? LogoUrl = null);
