using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace JobFinder.Web.Data;

/// <summary>
/// Calls the Google Gemini API (generateContent). Used to write cover letters and to read the text out of a PDF CV.
/// The free tier of gemini-2.5-flash is enough for personal use; the key comes from https://aistudio.google.com/apikey.
/// </summary>
public class GeminiClient(IHttpClientFactory httpFactory)
{
    public const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/";

    /// <summary>Waits between attempts when Gemini is busy: 4 attempts in all, about 20 seconds of waiting at most.</summary>
    private static readonly TimeSpan[] RetryDelays = { TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(10) };

    /// <summary>Sends one text prompt and returns the reply text.</summary>
    public Task<string> GenerateAsync(string apiKey, string model, string prompt, double temperature, CancellationToken ct = default)
    {
        var parts = new object[] { new { text = prompt } };
        return SendAsync(apiKey, model, parts, temperature, ct);
    }

    /// <summary>Asks Gemini to copy out all the text of a PDF (a CV), keeping its order and headings.</summary>
    public Task<string> ExtractPdfTextAsync(string apiKey, string model, byte[] pdf, CancellationToken ct = default)
    {
        var parts = new object[]
        {
            new { inline_data = new { mime_type = "application/pdf", data = Convert.ToBase64String(pdf) } },
            new { text = "This PDF is a CV. Copy out all of its text as plain text, in reading order. Keep headings on their own lines and put each bullet point on its own line starting with \"- \". Do not summarise, rewrite, correct or add anything. Output only the text." },
        };
        return SendAsync(apiKey, model, parts, 0, ct);
    }

    private async Task<string> SendAsync(string apiKey, string model, object[] parts, double temperature, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Add your Gemini API key on the Documents page first.");

        var body = new
        {
            contents = new[] { new { role = "user", parts } },
            generationConfig = new { temperature },
        };

        var http = httpFactory.CreateClient("gemini");
        string json;
        for (var attempt = 1; ; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"models/{Uri.EscapeDataString(model)}:generateContent")
            {
                Content = JsonContent.Create(body),
            };
            req.Headers.Add("x-goog-api-key", apiKey.Trim());

            using var res = await http.SendAsync(req, ct);
            json = await res.Content.ReadAsStringAsync(ct);
            if (res.IsSuccessStatusCode) break;

            // Busy (503/500/502/504) is usually over within seconds, so wait and try again a few times before giving up.
            var status = (int)res.StatusCode;
            var retryable = status is 500 or 502 or 503 or 504;
            if (!retryable || attempt >= RetryDelays.Length + 1)
                throw new HttpRequestException(ErrorMessage(status, json) + (retryable ? $" (tried {attempt} times)" : ""));

            await Task.Delay(RetryDelays[attempt - 1], ct);
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("candidates", out var cands) || cands.GetArrayLength() == 0)
        {
            var reason = root.TryGetProperty("promptFeedback", out var fb) && fb.TryGetProperty("blockReason", out var br)
                ? br.GetString() : "no reply";
            throw new HttpRequestException($"Gemini didn't return any text ({reason}).");
        }

        var cand = cands[0];
        var sb = new StringBuilder();
        if (cand.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var outParts))
        {
            foreach (var p in outParts.EnumerateArray())
            {
                // Skip the model's thinking, if it is ever returned.
                if (p.TryGetProperty("thought", out var th) && th.ValueKind == JsonValueKind.True) continue;
                if (p.TryGetProperty("text", out var t)) sb.Append(t.GetString());
            }
        }

        var text = sb.ToString().Trim();
        if (text == "")
        {
            var finish = cand.TryGetProperty("finishReason", out var fr) ? fr.GetString() : "unknown";
            throw new HttpRequestException($"Gemini returned an empty reply (finish reason: {finish}). Try again.");
        }
        return text;
    }

    private static string ErrorMessage(int status, string json)
    {
        string? message = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var e) && e.TryGetProperty("message", out var m))
                message = m.GetString();
        }
        catch (JsonException) { }

        var hint = status switch
        {
            400 or 403 => " Check the API key on the Documents page.",
            404 => " Check the model name on the Documents page.",
            429 => " The free tier's limit was reached. Wait a minute and try again.",
            503 => " Gemini is busy right now. Try again shortly.",
            _ => "",
        };
        message ??= json.Length > 200 ? json[..200] : json;
        return $"Gemini returned {status}: {message}{hint}".Trim();
    }
}
