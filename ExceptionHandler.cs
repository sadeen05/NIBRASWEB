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
        if (exception is BadHttpRequestException badRequest)
        {
            _logger.LogWarning("Request rejected with status code {StatusCode}.", badRequest.StatusCode);

            var badRequestStatus = badRequest.StatusCode;
            var isPayloadTooLarge = badRequestStatus == StatusCodes.Status413PayloadTooLarge;
            var badRequestProblem = new ProblemDetails
            {
                Status = badRequestStatus,
                Title = isPayloadTooLarge ? "Request body too large." : "Invalid request.",
                Detail = isPayloadTooLarge
                    ? "The request body exceeds the allowed size."
                    : "The request could not be processed."
            };
            badRequestProblem.Extensions["errorCode"] = isPayloadTooLarge ? "PAYLOAD_TOO_LARGE" : "BAD_REQUEST";
            badRequestProblem.Extensions["traceId"] = httpContext.TraceIdentifier;

            httpContext.Response.StatusCode = badRequestStatus;
            await httpContext.Response.WriteAsJsonAsync(badRequestProblem, ct);
            return true;
        }

        _logger.LogError(exception, "Unhandled exception");

        var (status, title, errorCode) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Resource not found.", "RESOURCE_NOT_FOUND"),
            ForbiddenException => (StatusCodes.Status403Forbidden, "Not authorized to perform this action.", "FORBIDDEN"),
            BusinessRuleException => (StatusCodes.Status409Conflict, "Request violates a business rule.", "INVALID_STATE"),
            ValidationFailedException => (StatusCodes.Status400BadRequest, "Invalid request.", "BAD_REQUEST"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.", "INTERNAL_ERROR")
        };

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = exception switch
            {
                NotFoundException or ForbiddenException
                    or BusinessRuleException or ValidationFailedException
                    => exception.Message,
                _ => "An unexpected error occurred."
            }
        };
        problem.Extensions["errorCode"] = errorCode;
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(problem, ct);
        return true;
    }
}
