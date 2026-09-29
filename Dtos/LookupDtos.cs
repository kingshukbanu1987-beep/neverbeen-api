namespace NeverBeen.API.Dtos;

public class CountryDto
{
    public int Id { get; set; }
    public string IsoCode2 { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? PhoneCode { get; set; }
}

public class CityDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
