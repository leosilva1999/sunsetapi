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
            ConflictException conflictException => (HttpStatusCode.Conflict, conflictException.Message, null),
            ExternalServiceException externalServiceException => (HttpStatusCode.BadGateway, externalServiceException.Message, null),
            _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred.", null),
        };

        if (statusCode == HttpStatusCode.InternalServerError)
            logger.LogError(exception, "Unhandled exception");

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var payload = new { title, errors };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
