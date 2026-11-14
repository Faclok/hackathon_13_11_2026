using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using TelegramBotKit;
using TelegramBotKit.Middleware;

internal static class BotDiagnosticErrors
{
    // Exception messages and request URLs can contain credentials or student data.
    public static string Describe(Exception exception) => string.Join(" -> ",
        Chain(exception).Select(error => error is SocketException socket
            ? $"{error.GetType().Name} ({socket.SocketErrorCode})"
            : error is Telegram.Bot.Exceptions.ApiRequestException telegram
                ? $"{error.GetType().Name} (Telegram code {telegram.ErrorCode})"
                : error is Npgsql.PostgresException postgres
                    ? $"{error.GetType().Name} (SQLSTATE {postgres.SqlState})"
                : error.GetType().Name));

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            yield return current;
    }
}

internal sealed class TelegramHttpDiagnostics(ILogger<TelegramHttpDiagnostics> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Only the method name is logged: the preceding URL segment contains the bot token.
        var method = request.RequestUri?.Segments.LastOrDefault()?.Trim('/') ?? "unknown";
        var timer = Stopwatch.StartNew();
        logger.LogInformation("Telegram API {Method}: request started", method);
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            logger.LogInformation("Telegram API {Method}: HTTP {Status} after {ElapsedMs} ms", method, (int)response.StatusCode, timer.ElapsedMilliseconds);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Telegram API {Method} rejected the request: HTTP {Status}", method, (int)response.StatusCode);
            return response;
        }
        catch (Exception ex)
        {
            logger.LogError("Telegram API {Method} failed after {ElapsedMs} ms: {Failure}", method, timer.ElapsedMilliseconds, BotDiagnosticErrors.Describe(ex));
            throw;
        }
    }
}

public sealed class UpdateDiagnostics(ILogger<UpdateDiagnostics> logger) : IUpdateMiddleware
{
    public async Task InvokeAsync(BotContext ctx, BotContextDelegate next)
    {
        var timer = Stopwatch.StartNew();
        logger.LogInformation("Update {UpdateId} received; type: {Type}", ctx.Update.Id, ctx.Update.Type);
        try
        {
            await next(ctx);
            logger.LogInformation("Update {UpdateId} pipeline completed after {ElapsedMs} ms", ctx.Update.Id, timer.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            logger.LogError("Update {UpdateId} failed after {ElapsedMs} ms: {Failure}", ctx.Update.Id, timer.ElapsedMilliseconds, BotDiagnosticErrors.Describe(ex));
            throw;
        }
    }
}
