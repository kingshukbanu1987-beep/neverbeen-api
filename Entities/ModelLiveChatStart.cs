using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NeverBeen.API.Entities;

/// <summary>
/// One Live Chat started with an AI model: written when a visitor's two-hour chat session begins.
/// The model's card counts these rows.
/// </summary>
[Table("ModelLiveChatStarts")]
public class ModelLiveChatStart
{
    public long Id { get; set; }

    [MaxLength(120)]
    public string ModelSlug { get; set; } = string.Empty;

    public int? ViewerId { get; set; }
    public UserProfile? Viewer { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
