using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeverBeen.API.Data;
using NeverBeen.API.Dtos;
using NeverBeen.API.Entities;

namespace NeverBeen.API.Controllers;

/// <summary>
/// Advertising pool for the NeverBeen AI model portfolios.
///
/// The website owns the portfolio catalog and renders the advertisement itself (the right rail of
/// the Community Home page and the sponsored cards inside the Journey feed). This controller only
/// answers which models the studio has switched on for advertising — with their serving weight —
/// and records how many members saw and opened each advertised portfolio, so the founder can see
/// which models the advertisement actually reaches.
///
/// Both endpoints are public: the advertisement is shown to guests and signed-out visitors exactly
/// as it is to signed-in members, and an anonymous view still has to be counted.
/// </summary>
[ApiController]
[Route("api/models/ads")]
public class ModelAdsController : ControllerBase
{
    private const int MaximumEventsPerRequest = 50;

    private static readonly Regex ModelSlugPattern = new(
        "^[a-z0-9]+(?:-[a-z0-9]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] EventTypes = { "impression", "click" };
    private static readonly string[] Surfaces = { "rail", "feed" };

    private readonly AppDbContext _db;

    public ModelAdsController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Every portfolio in the pool, heaviest rotation first. The website treats a model that is
    /// missing from this list as advertised (the studio advertises the whole catalog) and skips
    /// only the models whose row says <c>IsActive = false</c>.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ModelAdPoolDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ModelAdPoolDto>> Pool(CancellationToken cancellationToken = default)
    {
        var items = await _db.ModelAds
            .AsNoTracking()
            .OrderByDescending(ad => ad.Weight)
            .ThenBy(ad => ad.ModelSlug)
            .Select(ad => new ModelAdDto
            {
                ModelSlug = ad.ModelSlug,
                DisplayName = ad.DisplayName,
                IsActive = ad.IsActive,
                Weight = ad.Weight,
                ImpressionCount = ad.ImpressionCount,
                ClickCount = ad.ClickCount
            })
            .ToListAsync(cancellationToken);

        return Ok(new ModelAdPoolDto { Items = items });
    }

    /// <summary>
    /// Records a batch of advertisement impressions / clicks sent by the website. Events for a
    /// slug the pool does not know are ignored (the website never invents slugs, and ignoring them
    /// keeps an anonymous caller from growing the table), and the pool totals are updated in the
    /// same transaction.
    /// </summary>
    [HttpPost("events")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> RecordEvents(
        [FromBody] RecordModelAdEventsRequest request,
        CancellationToken cancellationToken = default)
    {
        var events = request?.Events;
        if (events == null || events.Count == 0)
            return BadRequest(new { error = "At least one advertisement event is required." });
        if (events.Count > MaximumEventsPerRequest)
            return BadRequest(new { error = $"At most {MaximumEventsPerRequest} advertisement events may be sent at once." });

        var normalised = new List<(string Slug, string Type, string Surface)>(events.Count);
        foreach (var entry in events)
        {
            var slug = entry.ModelSlug?.Trim() ?? string.Empty;
            var type = entry.Type?.Trim().ToLowerInvariant() ?? string.Empty;
            var surface = entry.Surface?.Trim().ToLowerInvariant() ?? string.Empty;

            if (!IsValidModelSlug(slug))
                return BadRequest(new { error = "An advertisement event carries an invalid model slug." });
            if (!EventTypes.Contains(type))
                return BadRequest(new { error = "An advertisement event type must be \"impression\" or \"click\"." });
            if (!Surfaces.Contains(surface))
                return BadRequest(new { error = "An advertisement surface must be \"rail\" or \"feed\"." });

            normalised.Add((slug, type, surface));
        }

        var viewerId = await ResolveViewerIdAsync(cancellationToken);
        var now = DateTime.UtcNow;

        var slugs = normalised.Select(entry => entry.Slug).Distinct().ToList();
        var pool = await _db.ModelAds
            .Where(ad => slugs.Contains(ad.ModelSlug))
            .ToListAsync(cancellationToken);

        var recorded = 0;
        foreach (var group in normalised.GroupBy(entry => entry.Slug))
        {
            var advertisement = pool.FirstOrDefault(ad => ad.ModelSlug == group.Key);
            if (advertisement == null) continue; // not part of the pool — nothing to count

            foreach (var entry in group)
            {
                _db.ModelAdEvents.Add(new ModelAdEvent
                {
                    ModelSlug = entry.Slug,
                    EventType = entry.Type,
                    Surface = entry.Surface,
                    ViewerId = viewerId,
                    CreatedAtUtc = now
                });
                recorded += 1;
            }

            advertisement.ImpressionCount += group.Count(entry => entry.Type == "impression");
            advertisement.ClickCount += group.Count(entry => entry.Type == "click");
            advertisement.UpdatedAtUtc = now;
        }

        if (recorded == 0)
            return Accepted(new { recorded });

        await _db.SaveChangesAsync(cancellationToken);
        return Accepted(new { recorded });
    }

    /// <summary>The signed-in member behind an advertisement view, or null for a guest / a stale token.</summary>
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
