using System.Net.Http.Json;

namespace JobFinder.Web.Data;

/// <summary>One push notification. <paramref name="Click"/> is the link opened when it is tapped.</summary>
public record NtfyMessage(string Title, string Message, string? Click = null, string[]? Tags = null);

/// <summary>
/// Publishes to the public ntfy.sh server. Messages go as JSON so titles and text can contain any
/// characters (macrons, dashes) without HTTP header encoding problems.
/// </summary>
public class NtfyClient(IHttpClientFactory httpFactory)
{
    public const string Server = "https://ntfy.sh";

    public static string SubscribeUrl(string topic) => $"{Server}/{topic}";

    public async Task SendAsync(string topic, NtfyMessage m, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object>
        {
            ["topic"] = topic,
            ["title"] = m.Title,
            ["message"] = m.Message,
        };
        if (!string.IsNullOrEmpty(m.Click)) body["click"] = m.Click;
        if (m.Tags is { Length: > 0 }) body["tags"] = m.Tags;

        var http = httpFactory.CreateClient("ntfy");
        using var res = await http.PostAsJsonAsync("/", body, ct);
        if (!res.IsSuccessStatusCode)
        {
            var text = (await res.Content.ReadAsStringAsync(ct)).Trim();
            if (text.Length > 200) text = text[..200];
            throw new HttpRequestException($"ntfy returned {(int)res.StatusCode} {res.ReasonPhrase} {text}".Trim());
        }
    }
}
