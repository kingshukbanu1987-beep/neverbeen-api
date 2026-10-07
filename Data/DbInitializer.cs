using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using NeverBeen.API.Entities;

namespace NeverBeen.API.Data;

public interface IDbInitializer
{
    Task InitializeAsync();
}

/// <summary>
/// Runs once at startup:
///  1. Creates the database schema. If EF migrations exist for this context they are
///     applied with <c>MigrateAsync()</c>; otherwise the schema is created from the
///     current EF model with <c>CreateTablesAsync()</c>.
///  2. Brings the Countries/Cities lookup tables - the lists behind the registration
///     and profile drop-downs - in step with <see cref="GeoSeedData"/> on every start,
///     so a database seeded from a shorter list (neverbeen-database/seed.sql ships 10
///     countries) still ends up with every country the API can validate a member against.
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
        else if (!await OurSchemaExistsAsync())
        {
            // NOTE: Database.EnsureCreatedAsync() is deliberately NOT used here.
            // EnsureCreated is a no-op whenever the database already contains ANY tables.
            // Managed PostgreSQL services such as Supabase always ship built-in tables in
            // their auth/storage/realtime schemas, so EnsureCreated would silently skip
            // creating the NeverBeen schema and every query would fail afterwards.
            // Instead we check for our own table and create the schema directly.
            _logger.LogInformation("No migrations found - creating the database schema from the EF model...");
            var creator = _db.GetService<IRelationalDatabaseCreator>();
            await creator.CreateTablesAsync();
        }
        else
        {
            _logger.LogInformation("Database schema already exists - skipping creation.");
        }

        // Countries/Cities are reconciled on every start (not only into an empty table):
        // a database seeded earlier from a shorter list - neverbeen-database/seed.sql has
        // 10 countries - would otherwise never receive the full registration list, and the
        // registration endpoint would reject every other country with
        // "The selected city does not belong to the selected country."
        await SyncLookupsAsync();
    }

    /// <summary>
    /// City rows an earlier seed stored under a name <see cref="GeoSeedData"/> has since
    /// renamed, so the registration page - which offers the current names - can still use them.
    /// The row keeps its id, so members already pointing at it stay valid.
    /// </summary>
    private static readonly (string IsoCode2, string Legacy, string Current)[] LegacyCityRenames =
    {
        ("DE", "Frankfurt", "Frankfurt am Main"),
        ("IN", "Bangalore", "Bengaluru"),
    };

    /// <summary>
    /// Brings Countries/Cities in step with <see cref="GeoSeedData"/>:
    ///  * a country that is already stored (matched by ISO code, then by name) keeps its id
    ///    and gains any missing cities;
    ///  * a country that is not stored yet is added with all of its cities, so a database
    ///    seeded from a short list still ends up with the full registration list;
    ///  * legacy city names are renamed in place;
    ///  * every insert is skipped when the row is already there, so running this again (or
    ///    starting a second instance) changes nothing.
    /// Rows are never deleted: members may already reference them.
    /// </summary>
    private async Task SyncLookupsAsync()
    {
        var seed = GeoSeedData.GetCountries();

        var countries = await _db.Countries.Include(c => c.Cities).ToListAsync();

        var byIso = new Dictionary<string, Country>(StringComparer.OrdinalIgnoreCase);
        var byName = new Dictionary<string, Country>(StringComparer.OrdinalIgnoreCase);
        foreach (var country in countries)
        {
            byIso[country.IsoCode2] = country;
            byName[Normalize(country.Name)] = country;
        }

        var addedCountries = 0;
        var addedCities = 0;
        var renamedCities = 0;

        foreach (var seeded in seed)
        {
            var country =
                byIso.TryGetValue(seeded.IsoCode2, out var byCode) ? byCode :
                byName.TryGetValue(Normalize(seeded.Name), out var byStoredName) ? byStoredName : null;

            if (country == null)
            {
                country = new Country
                {
                    IsoCode2 = seeded.IsoCode2,
                    Name = seeded.Name,
                    PhoneCode = seeded.PhoneCode
                };
                foreach (var city in seeded.Cities)
                    country.Cities.Add(new City { Name = city });

                _db.Countries.Add(country);
                byIso[seeded.IsoCode2] = country;
                byName[Normalize(seeded.Name)] = country;
                addedCountries++;
                addedCities += seeded.Cities.Count;
                continue;
            }

            // City rows of this country, keyed by a case- and accent-insensitive name.
            var stored = new Dictionary<string, City>(StringComparer.OrdinalIgnoreCase);
            foreach (var city in country.Cities)
                stored[Normalize(city.Name)] = city;

            foreach (var (isoCode2, legacyName, currentName) in LegacyCityRenames)
            {
                if (!string.Equals(isoCode2, seeded.IsoCode2, StringComparison.OrdinalIgnoreCase))
                    continue;

                var legacyKey = Normalize(legacyName);
                var currentKey = Normalize(currentName);
                // Only when the renamed row is still the one the API offers (not when a row
                // with the current name already exists as well).
                if (stored.TryGetValue(legacyKey, out var legacy) && !stored.ContainsKey(currentKey))
                {
                    legacy.Name = currentName;
                    stored.Remove(legacyKey);
                    stored[currentKey] = legacy;
                    renamedCities++;
                }
            }

            foreach (var city in seeded.Cities)
            {
                var key = Normalize(city);
                if (stored.ContainsKey(key))
                    continue;

                var entity = new City { Name = city };
                country.Cities.Add(entity);
                stored[key] = entity;
                addedCities++;
            }
        }

        if (addedCountries == 0 && addedCities == 0 && renamedCities == 0)
        {
            _logger.LogInformation(
                "Lookup data is already up to date ({CountryCount} countries).", seed.Count);
            return;
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            // A concurrent first start can win the race for the same rows - the unique index
            // then rejects the duplicate. The next start finishes the job; the API must never
            // fail to boot because of lookup data.
            _logger.LogWarning(ex, "Lookup sync could not be saved - it will be retried on the next start.");
            return;
        }

        _logger.LogInformation(
            "Lookup sync complete: {AddedCountries} countries and {AddedCities} cities added, {RenamedCities} cities renamed.",
            addedCountries, addedCities, renamedCities);
    }

    /// <summary>Case- and accent-insensitive key used to compare names between seeds.</summary>
    private static string Normalize(string name)
    {
        var decomposed = name.Normalize(NormalizationForm.FormD);
        var letters = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                letters.Append(ch);
        }

        var key = new StringBuilder(letters.Length);
        foreach (var ch in letters.ToString())
            key.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');

        return string.Join(' ', key.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Returns true when the table that backs <see cref="UserProfile"/> already exists,
    /// i.e. the schema has been created before. Uses the table/schema names from the EF
    /// model so it keeps working if they are ever remapped.
    /// </summary>
    private async Task<bool> OurSchemaExistsAsync()
    {
        var entityType = _db.Model.FindEntityType(typeof(UserProfile))
            ?? throw new InvalidOperationException("UserProfile is not part of the EF model.");
        var tableName = entityType.GetTableName() ?? nameof(UserProfile);
        var schema = entityType.GetSchema() ?? "public";

        var connection = _db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed)
            await connection.OpenAsync();

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT EXISTS (SELECT 1 FROM information_schema.tables " +
                "WHERE table_schema = @schema AND table_name = @table)";

            var schemaParam = command.CreateParameter();
            schemaParam.ParameterName = "@schema";
            schemaParam.Value = schema;
            command.Parameters.Add(schemaParam);

            var tableParam = command.CreateParameter();
            tableParam.ParameterName = "@table";
            tableParam.Value = tableName;
            command.Parameters.Add(tableParam);

            var result = await command.ExecuteScalarAsync();
            return result is true;
        }
        finally
        {
            if (wasClosed)
                await connection.CloseAsync();
        }
    }
}
