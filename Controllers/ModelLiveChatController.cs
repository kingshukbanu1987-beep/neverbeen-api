using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeverBeen.API.Data;
using NeverBeen.API.Dtos;
using NeverBeen.API.Entities;

namespace NeverBeen.API.Controllers;

/// <summary>
/// Live Chat counters for the AI model directory. Each model's card shows how many chat sessions
/// are live with her right now and how many chats are already over. A session is counted live
/// when the visitor's two-hour session begins, and moves to the past count when its end is
/// recorded — or when the two hours run out, for sessions whose tab never said goodbye.
///
/// Reading the counts is public. Starting or ending a chat needs no sign-in: a signed-in member
/// is recorded with their account, a guest without one.
/// </summary>
[ApiController]
[Route("api/models")]
public class ModelLiveChatController : ControllerBase
{
    /// <summary>A chat session lasts two hours; an unended start older than that is past.</summary>
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(2);

    private static readonly Regex ModelSlugPattern = new(
        "^[a-z0-9]+(?:-[a-z0-9]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SessionKeyPattern = new(
        "^[A-Za-z0-9-]{8,64}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly AppDbContext _db;

    public ModelLiveChatController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>Returns the live and past chat counts of every model that has had a chat.</summary>
    [HttpGet("live-chats")]
    [ProducesResponseType(typeof(List<ModelLiveChatCountDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ModelLiveChatCountDto>>> GetCounts(
        CancellationToken cancellationToken = default)
    {
        var liveSince = DateTime.UtcNow - SessionLifetime;

        var rows = await _db.ModelLiveChatStarts
            .AsNoTracking()
            .GroupBy(s => s.ModelSlug)
            .Select(g => new ModelLiveChatCountDto
            {
                ModelSlug = g.Key,
                LiveNowCount = g.Count(s => s.EndedAtUtc == null && s.CreatedAtUtc > liveSince),
                PastChatCount = g.Count(s => s.EndedAtUtc != null || s.CreatedAtUtc <= liveSince)
            })
            .OrderBy(r => r.ModelSlug)
            .ToListAsync(cancellationToken);

        return Ok(rows);
    }

    /// <summary>Records that a visitor started a Live Chat, and returns the fresh counts.</summary>
    [HttpPost("{modelSlug}/live-chats")]
    [ProducesResponseType(typeof(ModelLiveChatStartDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ModelLiveChatStartDto>> RecordStart(
        [FromRoute] string modelSlug,
        [FromBody] ModelLiveChatSessionRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidModelSlug(modelSlug))
            return BadRequest(new { error = "The model slug is invalid." });

        var sessionKey = NormalizeSessionKey(request?.SessionKey);

        // One count per session: the same key is never counted twice, however often it is sent.
        var alreadyCounted = sessionKey != null && await _db.ModelLiveChatStarts.AnyAsync(
            s => s.ModelSlug == modelSlug && s.SessionKey == sessionKey,
            cancellationToken);

        if (!alreadyCounted)
        {
            var viewerId = await ResolveViewerIdAsync(cancellationToken);

            _db.ModelLiveChatStarts.Add(new ModelLiveChatStart
            {
                ModelSlug = modelSlug,
                SessionKey = sessionKey,
                ViewerId = viewerId,
                CreatedAtUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(cancellationToken);
        }

        return Ok(await CountsForAsync(modelSlug, sessionKey, cancellationToken));
    }

    /// <summary>Records that a Live Chat session ended, and returns the fresh counts.</summary>
    [HttpPost("{modelSlug}/live-chats/end")]
    [ProducesResponseType(typeof(ModelLiveChatStartDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ModelLiveChatStartDto>> RecordEnd(
        [FromRoute] string modelSlug,
        [FromBody] ModelLiveChatSessionRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidModelSlug(modelSlug))
            return BadRequest(new { error = "The model slug is invalid." });

        var sessionKey = NormalizeSessionKey(request?.SessionKey);

        if (sessionKey != null)
        {
            var start = await _db.ModelLiveChatStarts.FirstOrDefaultAsync(
                s => s.ModelSlug == modelSlug && s.SessionKey == sessionKey,
                cancellationToken);

            // Ending is idempotent: an unknown key, or one that ended already, changes nothing.
            if (start != null && start.EndedAtUtc == null)
            {
                start.EndedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        return Ok(await CountsForAsync(modelSlug, sessionKey, cancellationToken));
    }

    private async Task<ModelLiveChatStartDto> CountsForAsync(
        string modelSlug,
        string? sessionKey,
        CancellationToken cancellationToken)
    {
        var liveSince = DateTime.UtcNow - SessionLifetime;

        var liveNow = await _db.ModelLiveChatStarts.CountAsync(
            s => s.ModelSlug == modelSlug && s.EndedAtUtc == null && s.CreatedAtUtc > liveSince,
            cancellationToken);
        var past = await _db.ModelLiveChatStarts.CountAsync(
            s => s.ModelSlug == modelSlug && (s.EndedAtUtc != null || s.CreatedAtUtc <= liveSince),
            cancellationToken);

        return new ModelLiveChatStartDto
        {
            ModelSlug = modelSlug,
            SessionKey = sessionKey,
            LiveNowCount = liveNow,
            PastChatCount = past
        };
    }

    private async Task<int?> ResolveViewerIdAsync(CancellationToken cancellationToken)
    {
        if (User?.Identity?.IsAuthenticated != true) return null;
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(raw, out var userId)) return null;
        return await _db.Users.AnyAsync(user => user.Id == userId, cancellationToken) ? userId : null;
    }

    private static bool IsValidModelSlug(string? modelSlug) =>
        !string.IsNullOrWhiteSpace(modelSlug) &&
        modelSlug.Length <= 120 &&
        ModelSlugPattern.IsMatch(modelSlug);

    private static string? NormalizeSessionKey(string? sessionKey)
    {
        var trimmed = sessionKey?.Trim();
        return !string.IsNullOrEmpty(trimmed) && SessionKeyPattern.IsMatch(trimmed) ? trimmed : null;
    }
}
