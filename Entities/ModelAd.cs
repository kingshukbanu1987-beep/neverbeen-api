using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NeverBeen.API.Entities;

/// <summary>
/// One AI model portfolio the NeverBeen website advertises — the right-rail panel below
/// “Online Companions” and the sponsored cards inside the Journey feed.
///
/// The website owns the portfolio catalog (name, photographs, city and availability come from
/// `data/ai-models/profiles.json` in the website repository); this row only decides whether the
/// model takes part in the advertising rotation, how heavily she is rotated, and keeps the
/// impression / click totals for the founder's reporting.
/// </summary>
[Table("ModelAds")]
public class ModelAd
{
    public int Id { get; set; }

    /// <summary>Stable URL slug of the portfolio shown at /ai-models/{slug} on the website.</summary>
    [MaxLength(120)]
    public string ModelSlug { get; set; } = string.Empty;

    /// <summary>Display name as published by the website — kept for reporting only.</summary>
    [MaxLength(160)]
    public string? DisplayName { get; set; }

    /// <summary>False removes the model from the advertising rotation without deleting her row.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Rotation weight 1..10 — a heavier model is drawn more often by the website.</summary>
    public int Weight { get; set; } = 1;

    public int ImpressionCount { get; set; }

    public int ClickCount { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAtUtc { get; set; }
}
