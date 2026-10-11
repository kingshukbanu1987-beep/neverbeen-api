using System.ComponentModel.DataAnnotations;

namespace NeverBeen.API.Dtos;

/// <summary>One AI model portfolio in the advertising pool, with her weight and totals.</summary>
public class ModelAdDto
{
    public string ModelSlug { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public bool IsActive { get; set; }
    public int Weight { get; set; }
    public int ImpressionCount { get; set; }
    public int ClickCount { get; set; }
}

/// <summary>Answer of GET /api/models/ads — every advertised portfolio, heaviest rotation first.</summary>
public class ModelAdPoolDto
{
    public List<ModelAdDto> Items { get; set; } = new();
}

/// <summary>One impression or click of an advertised portfolio.</summary>
public class ModelAdEventRequest
{
    [Required]
    [StringLength(120, MinimumLength = 2)]
    public string ModelSlug { get; set; } = string.Empty;

    /// <summary>"impression" or "click".</summary>
    [Required]
    [StringLength(16)]
    public string Type { get; set; } = string.Empty;

    /// <summary>"rail" (right rail of the Community Home) or "feed" (Journey feed card).</summary>
    [Required]
    [StringLength(16)]
    public string Surface { get; set; } = string.Empty;
}

/// <summary>Body of POST /api/models/ads/events — one batch per advertisement view.</summary>
public class RecordModelAdEventsRequest
{
    [Required]
    [MinLength(1)]
    [MaxLength(50)]
    public List<ModelAdEventRequest> Events { get; set; } = new();
}
