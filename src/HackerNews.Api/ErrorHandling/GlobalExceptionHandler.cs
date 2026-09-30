using FluentValidation;
using HackerNews.Application.Abstractions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace HackerNews.Api.ErrorHandling;

/// <summary>Maps exceptions to RFC 7807 ProblemDetails responses.</summary>
internal sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    private const string UpstreamRetryAfterSeconds = "30";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // Client went away; nobody is listening for a response.
            return true;
        }

        var problem = exception switch
        {
            ValidationException validation => new HttpValidationProblemDetails(
                validation.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            },
            BadHttpRequestException badRequest => new ProblemDetails
            {
                Status = badRequest.StatusCode,
                Title = "Bad request.",
                Detail = badRequest.Message,
            },
            HackerNewsUnavailableException => new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Upstream service unavailable.",
                Detail = "Hacker News is currently unreachable and no cached data is available. Please retry later.",
            },
            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
            },
        };

        switch (problem.Status)
        {
            case StatusCodes.Status503ServiceUnavailable:
                LogUpstreamUnavailable(exception);
                httpContext.Response.Headers.RetryAfter = UpstreamRetryAfterSeconds;
                break;
            case StatusCodes.Status500InternalServerError:
                LogUnhandled(exception);
                break;
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Hacker News unavailable and no cached data to serve")]
    private partial void LogUpstreamUnavailable(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception")]
    private partial void LogUnhandled(Exception exception);
}
