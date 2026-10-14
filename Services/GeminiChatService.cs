using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NeverBeen.API.Services;

/// <summary>The outcome of one Gemini call.</summary>
public sealed record GeminiChatResult(bool Success, string Reply, string Error, int UpstreamStatus = 0)
{
    public static GeminiChatResult Ok(string reply) => new(true, reply, string.Empty);
    public static GeminiChatResult Fail(string error, int upstreamStatus = 0) => new(false, string.Empty, error, upstreamStatus);
}

/// <summary>
/// Calls Google Gemini (<c>gemini-3.6-flash</c>) for the AI model Live Chat.
///
/// The API key is a server-side secret read from the <c>GEMINI_API_KEY</c> environment variable
/// (Azure App Service → Configuration → Application settings, or
/// <c>dotnet user-secrets set GEMINI_API_KEY …</c> locally). It is sent to Google in the
/// <c>x-goog-api-key</c> header — never in the URL, never in a log line and never back to the
/// browser.
/// </summary>
public sealed class GeminiChatService
{
    /// <summary>The environment variable / configuration key that holds the Gemini API key.</summary>
    public const string ApiKeySetting = "GEMINI_API_KEY";

    /// <summary>The Gemini model used by the AI model Live Chat.</summary>
    public const string ChatModel = "gemini-3.6-flash";

    private const string EndpointTemplate =
        "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent";

    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GeminiChatService> _logger;

    public GeminiChatService(HttpClient http, IConfiguration configuration, ILogger<GeminiChatService> logger)
    {
        _http = http;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// The key from configuration (environment variables are part of it), falling back to the raw
    /// environment variable so a host that bypasses the configuration system still works.
    /// </summary>
    private string ApiKey =>
        (_configuration[ApiKeySetting] ?? Environment.GetEnvironmentVariable(ApiKeySetting) ?? string.Empty).Trim();

    /// <summary>True when <c>GEMINI_API_KEY</c> is set.</summary>
    public bool IsConfigured => ApiKey.Length > 0;

    /// <summary>Asks Gemini for her answer to one turn.</summary>
    public async Task<GeminiChatResult> GenerateReplyAsync(AiChatTurn turn, CancellationToken cancellationToken)
    {
        var apiKey = ApiKey;
        if (apiKey.Length == 0) return GeminiChatResult.Fail("ai-unavailable");

        var body = new
        {
            systemInstruction = new { parts = new[] { new { text = AiChatPrompt.BuildSystemInstruction(turn) } } },
            contents = AiChatPrompt.BuildContents(turn)
                .Select(content => new { role = content.Role, parts = new[] { new { text = content.Text } } })
                .ToArray(),
            generationConfig = new
            {
                temperature = 0.85,
                topP = 0.95,
                maxOutputTokens = 2048,
                // Gemini 3 uses thinkingLevel rather than the Gemini 2.5 token budget.
                thinkingConfig = new { thinkingLevel = "low" },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, string.Format(EndpointTemplate, ChatModel))
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-goog-api-key", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Gemini chat request timed out.");
            return GeminiChatResult.Fail("ai-timeout");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Gemini chat request could not be sent.");
            return GeminiChatResult.Fail("ai-failed");
        }

        using (response)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // The upstream body can echo the request but never the key (it travels in a header).
                _logger.LogWarning(
                    "Gemini chat answered {StatusCode}: {Body}",
                    (int)response.StatusCode,
                    payload.Length > 500 ? payload[..500] : payload);
                return GeminiChatResult.Fail("ai-failed", (int)response.StatusCode);
            }

            var reply = ExtractText(payload);
            return reply.Length > 0 ? GeminiChatResult.Ok(reply) : GeminiChatResult.Fail("ai-empty");
        }
    }

    /// <summary>
    /// The answer text of a <c>generateContent</c> response: every text part of the first
    /// candidate, skipping thought summaries. Empty when the answer was blocked or is missing.
    /// </summary>
    public static string ExtractText(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("candidates", out var candidates) ||
                candidates.ValueKind != JsonValueKind.Array ||
                candidates.GetArrayLength() == 0)
                return string.Empty;

            var first = candidates[0];
            if (!first.TryGetProperty("content", out var content) ||
                !content.TryGetProperty("parts", out var parts) ||
                parts.ValueKind != JsonValueKind.Array)
                return string.Empty;

            var text = new StringBuilder();
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True)
                    continue;
                if (part.TryGetProperty("text", out var value) && value.ValueKind == JsonValueKind.String)
                    text.Append(value.GetString());
            }

            return text.ToString().Trim();
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }
}
