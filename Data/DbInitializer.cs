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
