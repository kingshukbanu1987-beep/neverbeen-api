using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeverBeen.API.Common;
using NeverBeen.API.Data;
using NeverBeen.API.Dtos;
using NeverBeen.API.Entities;

namespace NeverBeen.API.Controllers;

/// <summary>
/// Public testimonials for each AI model portfolio. Reading is public; creating a testimonial
/// requires a completed, active NeverBeen Community account.
/// </summary>
[ApiController]
[Route("api/models/{modelSlug}/testimonials")]
public class ModelTestimonialsController : ControllerBase
{
    private const int MaximumPageSize = 100;
    private static readonly Regex ModelSlugPattern = new(
        "^[a-z0-9]+(?:-[a-z0-9]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly AppDbContext _db;

    public ModelTestimonialsController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>Returns a page of testimonials, newest first. This endpoint is public.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ModelTestimonialDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ModelTestimonialDto>>> List(
        [FromRoute] string modelSlug,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidModelSlug(modelSlug))
            return BadRequest(new { error = "The model slug is invalid." });

        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > MaximumPageSize) pageSize = MaximumPageSize;

        var query = _db.ModelTestimonials
            .AsNoTracking()
            .Where(testimonial => testimonial.ModelSlug == modelSlug)
            .Include(testimonial => testimonial.Author)
                .ThenInclude(author => author!.Country)
            .Include(testimonial => testimonial.Author)
                .ThenInclude(author => author!.City);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(testimonial => testimonial.CreatedAtUtc)
            .ThenByDescending(testimonial => testimonial.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Ok(new PagedResult<ModelTestimonialDto>
        {
            Items = items.Select(ToDto).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    /// <summary>Creates a half-star-step testimonial as the signed-in active Community member.</summary>
    [Authorize]
    [HttpPost]
    [ProducesResponseType(typeof(ModelTestimonialDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<ModelTestimonialDto>> Create(
        [FromRoute] string modelSlug,
        [FromBody] CreateModelTestimonialRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidModelSlug(modelSlug))
            return BadRequest(new { error = "The model slug is invalid." });

        var headline = request.Headline?.Trim() ?? string.Empty;
        var text = request.Text?.Trim() ?? string.Empty;
        if (headline.Length is < 10 or > 100)
            return BadRequest(new { error = "A headline must contain between 10 and 100 characters." });
        if (text.Length is < 20 or > 2500)
            return BadRequest(new { error = "A testimonial must contain between 20 and 2,500 characters." });
        if (request.Rating < 0.5m || request.Rating > 5m || (request.Rating * 2m) % 1m != 0m)
            return BadRequest(new { error = "A rating must be from 0.5 to 5 stars in half-star steps." });

        var author = await _db.Users
            .Include(user => user.Country)
            .Include(user => user.City)
            .FirstOrDefaultAsync(user => user.Id == User.GetUserId(), cancellationToken);
        if (author == null || !string.Equals(author.Status, UserProfileStatus.Active, StringComparison.OrdinalIgnoreCase))
            return Forbid();

        var testimonial = new ModelTestimonial
        {
            ModelSlug = modelSlug,
            Headline = headline,
            Text = text,
            Rating = request.Rating,
            AuthorId = author.Id,
            Author = author,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.ModelTestimonials.Add(testimonial);
        await _db.SaveChangesAsync(cancellationToken);

        var response = ToDto(testimonial);
        return CreatedAtAction(nameof(List), new { modelSlug, page = 1, pageSize = 50 }, response);
    }

    private static bool IsValidModelSlug(string? modelSlug) =>
        !string.IsNullOrWhiteSpace(modelSlug) &&
        modelSlug.Length <= 120 &&
        ModelSlugPattern.IsMatch(modelSlug);

    private static ModelTestimonialDto ToDto(ModelTestimonial testimonial) => new()
    {
        Id = testimonial.Id,
        ModelSlug = testimonial.ModelSlug,
        Headline = testimonial.Headline,
        Text = testimonial.Text,
        Rating = testimonial.Rating,
        CreatedAtUtc = testimonial.CreatedAtUtc,
        Author = testimonial.Author == null ? new AuthorDto() : AuthorMapper.From(testimonial.Author)
    };
}
