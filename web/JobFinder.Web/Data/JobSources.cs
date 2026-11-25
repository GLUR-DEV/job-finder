namespace JobFinder.Web.Data;

/// <summary>Which job site a job was scraped from. Stored in <see cref="Job.Source"/> and used by the Source filter.</summary>
public static class JobSources
{
    public const string Seek = "seek";
    public const string TradeMe = "trademe";

    /// <summary>Trade Me job ids are stored with this prefix so they can never clash with a SEEK id.</summary>
    public const string TradeMeIdPrefix = "tm-";

    public static string Label(string source) => source switch
    {
        TradeMe => "Trade Me",
        _ => "SEEK",
    };
}
