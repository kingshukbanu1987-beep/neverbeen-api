using System.ComponentModel.DataAnnotations;

namespace NeverBeen.API.Dtos;

/// <summary>Body of POST /api/models/{modelSlug}/testimonials.</summary>
public class CreateModelTestimonialRequest
{
    [Required]
    [StringLength(100, MinimumLength = 10)]
    public string Headline { get; set; } = string.Empty;

    [Required]
    [StringLength(2500, MinimumLength = 20)]
    public string Text { get; set; } = string.Empty;

    [Range(typeof(decimal), "0.5", "5.0")]
    public decimal Rating { get; set; }
}

/// <summary>A model testimonial with the Community author card displayed on the website.</summary>
public class ModelTestimonialDto
{
    public int Id { get; set; }
    public string ModelSlug { get; set; } = string.Empty;
    public string Headline { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public decimal Rating { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public AuthorDto Author { get; set; } = new();
}
