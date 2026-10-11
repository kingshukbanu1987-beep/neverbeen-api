using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NeverBeen.API.Entities;

/// <summary>A Community member's public rating and testimonial for an AI model portfolio.</summary>
[Table("ModelTestimonials")]
public class ModelTestimonial
{
    public int Id { get; set; }

    /// <summary>Stable URL slug of the model portfolio (the model catalog is managed by the website).</summary>
    [MaxLength(120)]
    public string ModelSlug { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Headline { get; set; } = string.Empty;

    [MaxLength(2500)]
    public string Text { get; set; } = string.Empty;

    [Column(TypeName = "numeric(2,1)")]
    public decimal Rating { get; set; }

    public int AuthorId { get; set; }
    public UserProfile? Author { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
