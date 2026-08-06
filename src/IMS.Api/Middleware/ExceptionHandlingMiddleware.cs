using System.Text.Json;
using FluentValidation;
using IMS.Application.Features.Auth;
using IMS.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace IMS.Api.Middleware;

/// <summary>
/// Translates every exception into a consistent RFC7807-style JSON payload, so no
/// controller needs its own try/catch and no stack trace ever reaches a client.
///
/// Doc §11 rule violations keep their machine-readable ErrorCode, and an insufficient-stock
/// failure carries its per-item shortfall list - required by acceptance scenario 3.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>Non-standard status popularised by nginx, used for client-cancelled requests.</summary>
    private const int ClientClosedRequest = 499;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
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
        var (status, errorCode, title, extensions) = Map(exception);

        // Expected rule violations are information, not incidents; only genuine faults
        // are logged at Error level with a stack trace.
        if (status >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception,
                "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogInformation(
                "Request rejected on {Method} {Path}: {ErrorCode} - {Message}",
                context.Request.Method, context.Request.Path, errorCode, exception.Message);
        }

        if (context.Response.HasStarted)
        {
            _logger.LogWarning("Response already started; cannot write error payload.");
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        var payload = new Dictionary<string, object?>
        {
            ["type"] = $"https://httpstatuses.io/{status}",
            ["title"] = title,
            ["status"] = status,
            ["errorCode"] = errorCode,
            ["detail"] = status >= StatusCodes.Status500InternalServerError && !_environment.IsDevelopment()
                ? "An unexpected error occurred. Please contact support with the trace id."
                : exception.Message,
            ["instance"] = context.Request.Path.Value,
            ["traceId"] = context.TraceIdentifier
        };

        foreach (var (key, value) in extensions) payload[key] = value;

        if (_environment.IsDevelopment() && status >= StatusCodes.Status500InternalServerError)
            payload["stackTrace"] = exception.StackTrace;

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static (int Status, string ErrorCode, string Title, Dictionary<string, object?> Extensions) Map(
        Exception exception)
    {
        var extensions = new Dictionary<string, object?>();

        switch (exception)
        {
            case AuthenticationFailedException auth:
                return (StatusCodes.Status401Unauthorized, auth.ErrorCode, "Authentication failed", extensions);

            case NotFoundException notFound:
                return (StatusCodes.Status404NotFound, notFound.ErrorCode, "Resource not found", extensions);

            case DuplicateEntityException duplicate:
                return (StatusCodes.Status409Conflict, duplicate.ErrorCode, "Duplicate entity", extensions);

            case ConcurrencyConflictException conflict:
                return (StatusCodes.Status409Conflict, conflict.ErrorCode,
                    "The record was modified by another operation. Please retry.", extensions);

            case InsufficientStockException stock:
                // Acceptance scenario 3: report which item is short and by how much.
                extensions["shortfalls"] = stock.Shortfalls.Select(s => new
                {
                    itemId = s.ItemId,
                    sku = s.Sku,
                    requestedQuantity = s.RequestedQuantity,
                    availableQuantity = s.AvailableQuantity,
                    shortQuantity = s.ShortQuantity
                }).ToList();

                return (StatusCodes.Status422UnprocessableEntity, stock.ErrorCode,
                    "Insufficient available stock", extensions);

            case InvalidStateTransitionException transition:
                return (StatusCodes.Status409Conflict, transition.ErrorCode,
                    "Invalid status transition", extensions);

            case BusinessRuleViolationException rule:
                if (rule.RuleNumber.HasValue) extensions["ruleNumber"] = rule.RuleNumber.Value;
                return (StatusCodes.Status422UnprocessableEntity, rule.ErrorCode,
                    "Business rule violation", extensions);

            case ValidationException validation:
                extensions["errors"] = validation.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

                return (StatusCodes.Status400BadRequest, "VALIDATION_FAILED",
                    "One or more validation errors occurred", extensions);

            case DbUpdateConcurrencyException:
                // Doc §11.11 - two operations touched the same stock row simultaneously.
                return (StatusCodes.Status409Conflict, "CONCURRENCY_CONFLICT",
                    "The record was modified by another operation. Please retry.", extensions);

            case DbUpdateException dbUpdate when IsUniqueViolation(dbUpdate):
                return (StatusCodes.Status409Conflict, "DUPLICATE_ENTITY",
                    "A record with the same unique key already exists", extensions);

            case UnauthorizedAccessException:
                return (StatusCodes.Status403Forbidden, "FORBIDDEN", "Access denied", extensions);

            case OperationCanceledException:
                return (ClientClosedRequest, "REQUEST_CANCELLED",
                    "The request was cancelled", extensions);

            default:
                return (StatusCodes.Status500InternalServerError, "INTERNAL_ERROR",
                    "An unexpected error occurred", extensions);
        }
    }

    /// <summary>PostgreSQL SQLSTATE 23505 = unique_violation.</summary>
    private static bool IsUniqueViolation(DbUpdateException exception)
        => exception.InnerException is Npgsql.PostgresException { SqlState: "23505" };
}
