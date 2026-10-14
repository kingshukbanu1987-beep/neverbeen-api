using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using NeverBeen.API.Data;
using NeverBeen.API.Middleware;
using NeverBeen.API.Services;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using NeverBeen.API.Controllers;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Database — Supabase (PostgreSQL) via Npgsql.
// Get the connection string from Supabase → Project Settings → Database
// (Session pooler recommended). See appsettings.json.
// ---------------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("Supabase")
    ?? builder.Configuration.GetConnectionString("NeverBeen")
    ?? "Host=localhost;Database=neverbeen;Username=postgres;Password=ChangeMe;SSL Mode=Prefer;";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// ---------------------------------------------------------------------------
// Authentication (JWT issued after the OAuth login) and authorization.
// ---------------------------------------------------------------------------
var jwtSection = builder.Configuration.GetSection("Jwt");
var signingKey = jwtSection["SigningKey"] ?? string.Empty;
if (Encoding.UTF8.GetByteCount(signingKey) < 32)
    throw new InvalidOperationException(
        "Jwt:SigningKey is not configured or is too short. Set a strong secret of at least 32 characters " +
        "(e.g. via user secrets or Azure App Configuration / Key Vault).");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"] ?? "NeverBeen.API",
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"] ?? "NeverBeen.Web",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(5)
        };
    });

builder.Services.AddAuthorization();

// ---------------------------------------------------------------------------
// Controllers + Swagger UI (available under /swagger in Development).
// ---------------------------------------------------------------------------
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "NeverBeen Community API",
        Version = "v1",
        Description = "The complete NeverBeen community backend on Supabase (PostgreSQL): OAuth (Google / Facebook / Microsoft) sign in and sign up, profiles, settings, galleries, Journey feeds, Message Book, messenger, companionships, followers, circles, notifications and moderation."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the JWT returned by POST /api/auth/oauth/login (e.g. \"Bearer eyJ...\")"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// ---------------------------------------------------------------------------
// OAuth login providers (Google, Facebook, Microsoft).
// ---------------------------------------------------------------------------
builder.Services.Configure<OauthOptions>(builder.Configuration.GetSection("OAuth"));
builder.Services.AddHttpClient();
builder.Services.AddScoped<IOauthLoginProvider, GoogleOauthLoginProvider>();
builder.Services.AddScoped<IOauthLoginProvider, FacebookOauthLoginProvider>();
builder.Services.AddScoped<IOauthLoginProvider, MicrosoftOauthLoginProvider>();

builder.Services.AddSingleton<JwtTokenService>();

// ---------------------------------------------------------------------------
// AI model Live Chat (POST /api/ai-chat) — Google Gemini gemini-2.5-flash.
// The key is a server-side secret: set the GEMINI_API_KEY environment variable
// (Azure App Service -> Configuration -> Application settings, or
// `dotnet user-secrets set GEMINI_API_KEY <key>` locally). Never put it in
// appsettings.json or in the website. Without it the endpoint answers 503 and
// the website falls back to the model's built-in engine.
// ---------------------------------------------------------------------------
builder.Services.AddHttpClient<GeminiChatService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(45);
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AiChatController.RateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            AiChatController.ClientKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = AiChatController.RequestsPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// ---------------------------------------------------------------------------
// CORS for the Angular frontend. List the allowed origins (comma separated)
// in Cors:AllowedOrigins. When the list is empty any origin is allowed -
// only use this during local development.
// ---------------------------------------------------------------------------
var corsOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (corsOrigins.Length == 0)
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        else
            policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod();
    });
});

// ---------------------------------------------------------------------------
// Health check (GET /health) including a database connectivity check.
// ---------------------------------------------------------------------------
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("neverbeen-db");

// ---------------------------------------------------------------------------
// Database initializer: creates the schema and seeds countries/cities.
// ---------------------------------------------------------------------------
builder.Services.AddScoped<IDbInitializer, DbInitializer>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<IDbInitializer>();
    await initializer.InitializeAsync();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();

// Swagger UI at /swagger - enabled in every environment so the deployed API can be
// explored and tested (e.g. on the VPS). Keep it behind Development only if you
// prefer not to expose the API documentation publicly.
app.UseSwagger();
app.UseSwaggerUI();

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
