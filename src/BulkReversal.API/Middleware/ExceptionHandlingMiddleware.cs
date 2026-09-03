using BulkReversal.Application.Common.Exceptions;
using BulkReversal.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace BulkReversal.API.Middleware;

/// <summary>
/// Translates Application/Domain exceptions into RFC 7807 ProblemDetails responses, so callers
/// (the Settlement-team SPA and, on the provider endpoints, the reversal engine) get a consistent,
/// machine-readable error shape instead of raw stack traces (also protects against information
/// disclosure for unhandled 500s).
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
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        var (statusCode, title) = exception switch
        {
            ValidationAppException => (StatusCodes.Status400BadRequest, "One or more validation errors occurred."),
            DomainException => (StatusCodes.Status400BadRequest, "The request violates a business rule."),
            NotFoundAppException => (StatusCodes.Status404NotFound, "Resource not found."),
            ConflictAppException => (StatusCodes.Status409Conflict, "The request conflicts with the current state."),
            ForbiddenAppException => (StatusCodes.Status403Forbidden, "You are not permitted to perform this action."),
            OperationCanceledException => (StatusCodes.Status499ClientClosedRequest, "Request was cancelled."),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.")
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception processing {Method} {Path}", context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogWarning(exception, "Request {Method} {Path} failed with {StatusCode}: {Message}",
                context.Request.Method, context.Request.Path, statusCode, exception.Message);
        }

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = statusCode;

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = statusCode == StatusCodes.Status500InternalServerError ? null : exception.Message,
            Instance = context.Request.Path
        };

        if (exception is ValidationAppException validationEx)
        {
            problemDetails.Extensions["errors"] = validationEx.Errors;
        }

        problemDetails.Extensions["traceId"] = context.TraceIdentifier;

        await context.Response.WriteAsJsonAsync(problemDetails);
    }
}
