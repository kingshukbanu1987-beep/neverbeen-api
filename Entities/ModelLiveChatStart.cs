using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NeverBeen.API.Entities;

/// <summary>
/// One Live Chat with an AI model: written when a visitor's two-hour session begins, and stamped
/// with its end when the session is over. The model's card counts the unended, unexpired rows as
/// live now, and every other row as a past chat.
/// </summary>
[Table("ModelLiveChatStarts")]
public class ModelLiveChatStart
{
    public long Id { get; set; }

    [MaxLength(120)]
    public string ModelSlug { get; set; } = string.Empty;

    /// <summary>The key the session was counted under, so its own end can be recorded.</summary>
    [MaxLength(64)]
    public string? SessionKey { get; set; }

    public int? ViewerId { get; set; }
    public UserProfile? Viewer { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>When the session ended, or null while it is live (or expired unended).</summary>
    public DateTime? EndedAtUtc { get; set; }
}
