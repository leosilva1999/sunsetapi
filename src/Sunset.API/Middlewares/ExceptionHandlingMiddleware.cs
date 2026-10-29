using System.Net;
using System.Text.Json;
using FluentValidation;
using Sunset.Application.Exceptions;

namespace Sunset.API.Middlewares;

public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            await HandleExceptionAsync(context, exception);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, title, errors) = exception switch
        {
            ValidationException validationException => (
                HttpStatusCode.BadRequest,
                "Validation failed.",
                validationException.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())),
            NotFoundException notFoundException => (HttpStatusCode.NotFound, notFoundException.Message, null),
            UnauthorizedActionException unauthorizedException => (HttpStatusCode.Unauthorized, unauthorizedException.Message, null),
            ForbiddenException forbiddenException => (HttpStatusCode.Forbidden, forbiddenException.Message,
                forbiddenException.Errors?.ToDictionary(e => e.Key, e => e.Value)),
            ConflictException conflictException => (HttpStatusCode.Conflict, conflictException.Message, null),
            ExternalServiceException externalServiceException => (HttpStatusCode.BadGateway, externalServiceException.Message, null),
            _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred.", null),
        };

        LogFailure(context, statusCode, exception);

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var payload = new { title, errors, requestId = context.TraceIdentifier };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }

    // 5xx are bugs or broken dependencies, so they keep the stack trace; 401 is a Warning because
    // a burst of them is the signal worth noticing (bad logins, token abuse); the rest (400/404/409)
    // are expected outcomes of client input and stay at Information, without a stack trace.
    private void LogFailure(HttpContext context, HttpStatusCode statusCode, Exception exception)
    {
        // This middleware sits before authentication, so the request-scoped logging context
        // doesn't know the user yet - read it straight off the (by now populated) principal.
        using var scope = context.User.FindFirst("sub")?.Value is { } userId
            ? logger.BeginScope(new Dictionary<string, object?> { ["UserId"] = userId })
            : null;

        // Field names only, never the submitted values (which can be passwords or emails).
        var errorMessage = exception is ValidationException validation
            ? $"Validation failed for: {string.Join(", ", validation.Errors.Select(e => e.PropertyName).Distinct())}"
            : exception.Message;

        switch (statusCode)
        {
            case HttpStatusCode.InternalServerError:
                logger.LogError(exception, "Unhandled exception");
                break;
            case HttpStatusCode.BadGateway:
                logger.LogError(exception, "Upstream dependency failed: {ErrorMessage}", exception.Message);
                break;
            case HttpStatusCode.Unauthorized:
                logger.LogWarning("Request rejected with {StatusCode} ({ExceptionType}): {ErrorMessage}",
                    (int)statusCode, exception.GetType().Name, errorMessage);
                break;
            default:
                logger.LogInformation("Request failed with {StatusCode} ({ExceptionType}): {ErrorMessage}",
                    (int)statusCode, exception.GetType().Name, errorMessage);
                break;
        }
    }
}
