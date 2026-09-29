using System.Text.Json;

namespace NeverBeen.API.Middleware;

/// <summary>
/// Catches unhandled exceptions and returns a ProblemDetails-style JSON error
/// instead of an HTML error page, so the Angular app always gets a parseable body.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception processing {Method} {Path}", context.Request.Method, context.Request.Path);

            if (context.Response.HasStarted)
                throw;

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";

            var problem = new
            {
                type = "https://httpstatuses.io/500",
                title = "Internal server error",
                status = 500,
                detail = "An unexpected error occurred. Please try again."
            };
            await context.Response.WriteAsync(JsonSerializer.Serialize(problem), context.RequestAborted);
        }
    }
}
