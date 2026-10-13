using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeverBeen.API.Data;
using NeverBeen.API.Dtos;
using NeverBeen.API.Entities;

namespace NeverBeen.API.Controllers;

/// <summary>
/// Live Chat counts for the AI model directory. Each model's card shows how many Live Chats have
/// been started with her. A chat is counted once, when the visitor's two-hour session begins.
///
/// Reading the counts is public. Starting a chat needs no sign-in: a signed-in member is recorded
/// with their account, a guest without one.
/// </summary>
[ApiController]
[Route("api/models")]
public class ModelLiveChatController : ControllerBase
{
    private static readonly Regex ModelSlugPattern = new(
        "^[a-z0-9]+(?:-[a-z0-9]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly AppDbContext _db;

    public ModelLiveChatController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>Returns the Live Chat count of every model that has had at least one chat.</summary>
    [HttpGet("live-chats")]
    [ProducesResponseType(typeof(List<ModelLiveChatCountDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ModelLiveChatCountDto>>> GetCounts(
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.ModelLiveChatStarts
            .AsNoTracking()
            .GroupBy(s => s.ModelSlug)
            .Select(g => new ModelLiveChatCountDto { ModelSlug = g.Key, LiveChatCount = g.Count() })
            .OrderBy(r => r.ModelSlug)
            .ToListAsync(cancellationToken);

        return Ok(rows);
    }

    /// <summary>Records that a visitor started a Live Chat with a model, and returns the new count.</summary>
    [HttpPost("{modelSlug}/live-chats")]
    [ProducesResponseType(typeof(ModelLiveChatStartDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ModelLiveChatStartDto>> RecordStart(
        [FromRoute] string modelSlug,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidModelSlug(modelSlug))
            return BadRequest(new { error = "The model slug is invalid." });

        var viewerId = await ResolveViewerIdAsync(cancellationToken);

        _db.ModelLiveChatStarts.Add(new ModelLiveChatStart
        {
            ModelSlug = modelSlug,
            ViewerId = viewerId,
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);

        var total = await _db.ModelLiveChatStarts.CountAsync(
            s => s.ModelSlug == modelSlug,
            cancellationToken);

        return Ok(new ModelLiveChatStartDto { ModelSlug = modelSlug, LiveChatCount = total });
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
}
