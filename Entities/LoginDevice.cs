using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NeverBeen.API.Entities;

/// <summary>A browser / phone / tablet the member has signed in from.</summary>
[Table("LoginDevices")]
public class LoginDevice
{
    /// <summary>Client-generated device id.</summary>
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = string.Empty;

    public int UserId { get; set; }

    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    /// <summary>"Phone", "Tablet", "Laptop" or "Desktop".</summary>
    [MaxLength(20)]
    public string Type { get; set; } = "Desktop";

    [MaxLength(60)]
    public string? Os { get; set; }

    [MaxLength(60)]
    public string? Browser { get; set; }

    [MaxLength(45)]
    public string? IpAddress { get; set; }

    [MaxLength(32)]
    public string? MacAddress { get; set; }

    [MaxLength(120)]
    public string? Location { get; set; }

    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    public bool IsCurrent { get; set; }
    public bool IsActive { get; set; } = true;
    public bool Blocked { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
