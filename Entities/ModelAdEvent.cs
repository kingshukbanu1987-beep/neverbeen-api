using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NeverBeen.API.Entities;

/// <summary>
/// One impression or click of an advertised AI model portfolio, as sent by the website in small
/// batches. Counter totals are also kept on <see cref="ModelAd"/> so the founder can read the
/// numbers without scanning this table; a nightly job may trim older events.
/// </summary>
[Table("ModelAdEvents")]
public class ModelAdEvent
{
    public long Id { get; set; }

    /// <summary>Slug of the advertised portfolio the member saw or opened.</summary>
    [MaxLength(120)]
    public string ModelSlug { get; set; } = string.Empty;

    /// <summary>"impression" or "click".</summary>
    [MaxLength(16)]
    public string EventType { get; set; } = string.Empty;

    /// <summary>Where the advertisement ran: "rail" (right rail panel) or "feed" (Journey feed).</summary>
    [MaxLength(16)]
    public string Surface { get; set; } = string.Empty;

    /// <summary>The signed-in member, when the advertisement was seen by one; null for guests.</summary>
    public int? ViewerId { get; set; }
    public UserProfile? Viewer { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
