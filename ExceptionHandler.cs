using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace NIBRAS;

public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        _logger.LogError(exception, "Unhandled exception");

        var env = httpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();

        var (status, title, errorCode) = exception switch
        {
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Resource not found.", "RESOURCE_NOT_FOUND"),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Not authorized to perform this action.", "FORBIDDEN"),
            InvalidOperationException => (StatusCodes.Status409Conflict, "Request violates a business rule.", "INVALID_STATE"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request.", "BAD_REQUEST"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.", "INTERNAL_ERROR")
        };

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = exception switch
            {
                KeyNotFoundException or UnauthorizedAccessException
                    or InvalidOperationException or ArgumentException
                    => exception.Message,
                _ when env.IsDevelopment() => exception.Message,
                _ => null
            }
        };
        problem.Extensions["errorCode"] = errorCode;
        if (env.IsDevelopment() && exception is not (
            KeyNotFoundException or UnauthorizedAccessException
            or InvalidOperationException or ArgumentException))
        {
            problem.Extensions["stackTrace"] = exception.StackTrace?.ToString();
        }

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(problem, ct);
        return true;
    }
}
