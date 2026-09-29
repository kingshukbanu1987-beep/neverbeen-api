using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NeverBeen.API.Entities;

[Table("Cities")]
public class City
{
    public int Id { get; set; }

    public int CountryId { get; set; }
    public Country? Country { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;
}
