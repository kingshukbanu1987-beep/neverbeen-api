using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeverBeen.API.Common;
using NeverBeen.API.Data;
using NeverBeen.API.Dtos;

namespace NeverBeen.API.Controllers;

/// <summary>
/// Drop-down data for the registration and profile pages:
/// countries, cities (based on the selected country) and the fixed
/// profession / gender option lists.
/// </summary>
[ApiController]
[Route("api/lookup")]
public class LookupController : ControllerBase
{
    private readonly AppDbContext _db;

    public LookupController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>All countries, alphabetically.</summary>
    [HttpGet("countries")]
    public async Task<ActionResult<List<CountryDto>>> Countries(CancellationToken cancellationToken)
    {
        return Ok(await _db.Countries
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CountryDto
            {
                Id = c.Id,
                IsoCode2 = c.IsoCode2,
                Name = c.Name,
                PhoneCode = c.PhoneCode
            })
            .ToListAsync(cancellationToken));
    }

    /// <summary>
    /// Cities of the selected country (optionally filtered with ?search=).
    /// Use this to fill the City drop-down after the user picks a Country.
    /// </summary>
    [HttpGet("countries/{id:int}/cities")]
    public async Task<ActionResult<List<CityDto>>> Cities(int id, [FromQuery] string? search, CancellationToken cancellationToken)
    {
        var country = await _db.Countries
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (country == null)
            return NotFound();

        var query = _db.Cities.AsNoTracking().Where(c => c.CountryId == id);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(c => c.Name.ToLower().Contains(search.ToLower()));

        return Ok(await query
            .OrderBy(c => c.Name)
            .Select(c => new CityDto { Id = c.Id, Name = c.Name })
            .ToListAsync(cancellationToken));
    }

    /// <summary>Fixed profession drop-down options.</summary>
    [HttpGet("professions")]
    public ActionResult<List<string>> Professions() => Ok(AppConstants.Professions.ToList());

    /// <summary>Fixed gender drop-down options.</summary>
    [HttpGet("genders")]
    public ActionResult<List<string>> Genders() => Ok(AppConstants.Genders.ToList());
}
