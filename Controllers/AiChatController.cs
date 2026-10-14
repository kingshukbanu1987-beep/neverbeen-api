using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NeverBeen.API.Dtos;
using NeverBeen.API.Services;

namespace NeverBeen.API.Controllers;

/// <summary>
/// The AI model Live Chat. The website sends the visitor's message together with the model's
/// character profile (her <c>personality/characterdatabase.json</c>) and her biography (her
/// <c>personality/biography.json</c>: her profile and her life story); this endpoint calls Google
/// Gemini (<c>gemini-2.5-flash</c>) with the secret <c>GEMINI_API_KEY</c> held only on the server,
/// and returns her finished answer.
///
/// The endpoint is public — guests chat too — so it is rate limited per client
/// (<see cref="RateLimitPolicy"/>). Without a configured key it answers
/// <c>503 { ok: false, error: "ai-unavailable" }</c> and the website answers from the model's own
/// built-in engine instead, so the chat always works.
/// </summary>
[ApiController]
[Route("api/ai-chat")]
public class AiChatController : ControllerBase
{
    /// <summary>The rate-limiter policy registered in <c>Program.cs</c>.</summary>
    public const string RateLimitPolicy = "ai-chat";

    /// <summary>Requests per client per minute.</summary>
    public const int RequestsPerMinute = 30;

    /// <summary>
    /// A turn is small: the message, the last messages, the dossier, the character profile and the
    /// biography (each of the two files is capped at 60 000 characters by <see cref="AiChatPrompt"/>).
    /// </summary>
    private const long MaxBodyBytes = 256 * 1024;

    private readonly GeminiChatService _gemini;

    public AiChatController(GeminiChatService gemini)
    {
        _gemini = gemini;
    }

    /// <summary>Answers one chat turn with Gemini.</summary>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicy)]
    [RequestSizeLimit(MaxBodyBytes)]
    [ProducesResponseType(typeof(AiChatResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AiChatResponse>> Reply(
        [FromBody] AiChatRequest? request,
        CancellationToken cancellationToken = default)
    {
        var (turn, error) = AiChatPrompt.Validate(request);
        if (turn is null)
            return BadRequest(new { ok = false, error = error ?? "invalid-payload" });

        if (!_gemini.IsConfigured)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { ok = false, error = "ai-unavailable" });

        var result = await _gemini.GenerateReplyAsync(turn, cancellationToken);
        if (!result.Success)
        {
            // Gemini's own quota limit is passed on as "busy", so the website retries on a later turn.
            var status = result.UpstreamStatus == StatusCodes.Status429TooManyRequests
                ? StatusCodes.Status429TooManyRequests
                : StatusCodes.Status502BadGateway;
            return StatusCode(status, new { ok = false, error = result.Error });
        }

        return Ok(new AiChatResponse { Reply = result.Reply, Model = GeminiChatService.ChatModel });
    }

    /// <summary>
    /// The rate-limit partition of a request: the visitor's address as the Cloudflare Worker
    /// passes it on (<c>CF-Connecting-IP</c>, then the first <c>X-Forwarded-For</c> entry),
    /// falling back to the connection's own address.
    /// </summary>
    public static string ClientKey(HttpContext context)
    {
        var cloudflare = context.Request.Headers["CF-Connecting-IP"].ToString().Trim();
        if (cloudflare.Length > 0) return cloudflare;

        var forwarded = context.Request.Headers["X-Forwarded-For"].ToString();
        var first = forwarded.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (!string.IsNullOrEmpty(first)) return first;

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
