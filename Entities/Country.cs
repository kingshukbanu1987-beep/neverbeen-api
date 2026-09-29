using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NeverBeen.API.Entities;

[Table("Countries")]
public class Country
{
    public int Id { get; set; }

    /// <summary>ISO 3166-1 alpha-2 code (e.g. "IN").</summary>
    [MaxLength(2)]
    public string IsoCode2 { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>International dial code, e.g. "+91".</summary>
    [MaxLength(8)]
    public string? PhoneCode { get; set; }

    public ICollection<City> Cities { get; set; } = new List<City>();
}
