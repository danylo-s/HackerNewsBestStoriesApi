using Mediator;
using Microsoft.Extensions.Logging;

namespace HackerNews.Application.Behaviors;

internal sealed class LoggingBehavior<TMessage, TResponse>(
    ILogger<LoggingBehavior<TMessage, TResponse>> logger,
    TimeProvider timeProvider)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        var name = typeof(TMessage).Name;
        var started = timeProvider.GetTimestamp();
        logger.Handling(name);

        try
        {
            var response = await next(message, cancellationToken);
            var elapsed = timeProvider.GetElapsedTime(started);
            logger.Handled(name, elapsed.TotalMilliseconds);
            return response;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var elapsed = timeProvider.GetElapsedTime(started);
            logger.Failed(ex, name, elapsed.TotalMilliseconds);
            throw;
        }
    }
}

internal static partial class LoggingBehaviorLog
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Handling {MessageName}")]
    public static partial void Handling(this ILogger logger, string messageName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {MessageName} in {ElapsedMs:0.0} ms")]
    public static partial void Handled(this ILogger logger, string messageName, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{MessageName} failed after {ElapsedMs:0.0} ms")]
    public static partial void Failed(this ILogger logger, Exception exception, string messageName, double elapsedMs);
}
