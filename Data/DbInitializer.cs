using Microsoft.EntityFrameworkCore;
using NeverBeen.API.Entities;

namespace NeverBeen.API.Data;

public interface IDbInitializer
{
    Task InitializeAsync();
}

/// <summary>
/// Runs once at startup:
///  1. Creates the database schema. If EF migrations exist for this context they are
///     applied with <c>MigrateAsync()</c>; otherwise <c>EnsureCreated()</c> creates the
///     full schema in one go (works directly against Azure SQL).
///  2. Seeds the Countries/Cities lookup tables used by the registration drop-downs.
/// </summary>
public class DbInitializer : IDbInitializer
{
    private readonly AppDbContext _db;
    private readonly ILogger<DbInitializer> _logger;

    public DbInitializer(AppDbContext db, ILogger<DbInitializer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        if (_db.Database.GetMigrations().Any())
        {
            _logger.LogInformation("Applying EF Core migrations...");
            await _db.Database.MigrateAsync();
        }
        else
        {
            _logger.LogInformation("No migrations found - creating the database schema with EnsureCreated...");
            await _db.Database.EnsureCreatedAsync();
        }

        if (!await _db.Countries.AnyAsync())
        {
            var seed = GeoSeedData.GetCountries();
            foreach (var country in seed)
            {
                var entity = new Country
                {
                    IsoCode2 = country.IsoCode2,
                    Name = country.Name,
                    PhoneCode = country.PhoneCode
                };
                foreach (var city in country.Cities)
                    entity.Cities.Add(new City { Name = city });
                _db.Countries.Add(entity);
            }

            await _db.SaveChangesAsync();
            _logger.LogInformation("Seeded {CountryCount} countries and their cities.", seed.Count);
        }
    }
}
