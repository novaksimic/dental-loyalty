using System.Diagnostics;
using DentalLoyalty.Application.Common.Exceptions;
using DentalLoyalty.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DentalLoyalty.Api.Errors;

// The single place an exception that escapes a controller becomes an HTTP
// response (SECURITY.md §10.7). Controllers keep their own intentional status
// codes; this only handles what they let through.
//
// Every response is a ProblemDetails with a fixed, generic title chosen here -
// never the exception's message, type, stack trace or inner exceptions, which
// can carry SQL, values or other internal detail. The traceId extension (added
// by AddProblemDetails) lets a client report an error that support can find in
// the logs.
//
// Logging: unexpected exceptions are logged as errors with the method, the path
// (never the query string, which can carry search terms) and the trace id.
// Request bodies, headers and tokens are never logged. Known, client-caused
// exceptions are logged at Information without the exception.
public sealed class ApiExceptionHandler : IExceptionHandler
{
    // Not an official status code; the de facto one for a request the client
    // abandoned (nobody reads the response).
    private const int ClientClosedRequest = 499;

    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<ApiExceptionHandler> _logger;

    public ApiExceptionHandler(
        IProblemDetailsService problemDetailsService,
        ILogger<ApiExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            httpContext.Response.StatusCode = ClientClosedRequest;
            return true;
        }

        var (status, title) = Map(exception);

        if (status >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(
                exception,
                "Unhandled exception for {Method} {Path}. TraceId {TraceId}.",
                httpContext.Request.Method,
                httpContext.Request.Path.Value,
                TraceId(httpContext));
        }
        else
        {
            _logger.LogInformation(
                "{ExceptionType} answered {StatusCode} for {Method} {Path}. TraceId {TraceId}.",
                exception.GetType().Name,
                status,
                httpContext.Request.Method,
                httpContext.Request.Path.Value,
                TraceId(httpContext));
        }

        httpContext.Response.StatusCode = status;

        // Exception is deliberately not passed to the context, so no writer can
        // add exception details to the response.
        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
            },
        });
    }

    // The same trace id AddProblemDetails puts in the response's traceId, so a
    // client-reported id finds the log entry.
    private static string TraceId(HttpContext httpContext) =>
        Activity.Current?.Id ?? httpContext.TraceIdentifier;

    public static (int Status, string Title) Map(Exception exception) => exception switch
    {
        ConcurrencyConflictException =>
            (StatusCodes.Status409Conflict, "The data was changed by another operation. Nothing was saved."),

        DuplicateEmailException
            or DuplicateReferralException
            or DuplicateRewardRedemptionException
            or DuplicateServiceOccurrenceException
            or ReferralAlreadyValidatedException =>
            (StatusCodes.Status409Conflict, "The request conflicts with existing data. Nothing was saved."),

        // A business rule (BUSINESS.md §20) the request would break.
        DomainException =>
            (StatusCodes.Status409Conflict, "The request conflicts with a business rule. Nothing was saved."),

        // Domain validation (Reward, Service, LoyaltyConfiguration, ...) throws
        // the ArgumentException family for invalid values. A null argument is a
        // programming error, not a client error.
        ArgumentNullException =>
            (StatusCodes.Status500InternalServerError, "An unexpected error occurred."),
        ArgumentException =>
            (StatusCodes.Status400BadRequest, "One or more values are invalid."),

        // Malformed requests rejected by the server itself (for example a body
        // that is too large).
        BadHttpRequestException badRequest =>
            (badRequest.StatusCode, "The request is invalid."),

        _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred."),
    };
}
