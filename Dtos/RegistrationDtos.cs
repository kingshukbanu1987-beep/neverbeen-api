using Microsoft.AspNetCore.Http;

namespace NeverBeen.API.Dtos;

/// <summary>
/// Body of POST /api/registration (multipart/form-data). Sent by the Registration Page
/// after a new user signed in via OAuth. Form field names must match the property names
/// (full-name is sent as "fullName" - form binding is case-insensitive).
/// </summary>
public class RegistrationRequest
{
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    /// <summary>Male / Female / Other.</summary>
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.MaxLength(20)]
    public string Gender { get; set; } = string.Empty;

    /// <summary>Picked from a calendar control; expected as "yyyy-MM-dd".</summary>
    [System.ComponentModel.DataAnnotations.Required]
    public DateTime DateOfBirth { get; set; }

    /// <summary>Id from GET /api/lookup/countries.</summary>
    [System.ComponentModel.DataAnnotations.Required]
    public int CountryId { get; set; }

    /// <summary>Id from GET /api/lookup/countries/{id}/cities (must belong to CountryId).</summary>
    [System.ComponentModel.DataAnnotations.Required]
    public int CityId { get; set; }

    [System.ComponentModel.DataAnnotations.MaxLength(20)]
    public string? Pincode { get; set; }

    /// <summary>Optional contact number.</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(30)]
    public string? ContactNumber { get; set; }

    /// <summary>Optional full postal address.</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(500)]
    public string? PostalAddress { get; set; }

    /// <summary>Optional "About Me".</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(2000)]
    public string? AboutMe { get; set; }

    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.EmailAddress]
    [System.ComponentModel.DataAnnotations.MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    /// <summary>One of the values from GET /api/lookup/professions.</summary>
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.MaxLength(60)]
    public string Profession { get; set; } = string.Empty;

    /// <summary>Optional profile photograph (JPEG/PNG/WebP/GIF, max 5 MB).</summary>
    public IFormFile? Photo { get; set; }
}
