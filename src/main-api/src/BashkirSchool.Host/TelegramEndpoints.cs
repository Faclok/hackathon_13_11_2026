using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BashkirSchool.Application;
using BashkirSchool.Infrastructure;
using Microsoft.EntityFrameworkCore;
using BashkirSchool.Host;

public static class TelegramEndpoints
{
    public static void MapTelegramEndpoints(this WebApplication app)
    {
        app.MapPost("/auth/telegram-mini-app", (TelegramMiniAppSignInRequest request, TelegramMiniAppAuth auth, CancellationToken ct) =>
            auth.SignInAsync(request.InitData, ct))
            .RequireRateLimiting("registration")
            .WithSummary("Open a session for a linked account using validated Telegram Mini App launch data.")
            .Produces<AccessTokenResponse>().ProducesProblem(401).ProducesProblem(404).ProducesProblem(429);
        app.MapPost("/api/me/telegram-link", async (ClaimsPrincipal user, TelegramStudents students, CancellationToken ct) =>
        {
            var link = await students.IssueLinkAsync(Guid.Parse(user.FindFirstValue("sub")!), ct);
            var username = app.Configuration["Telegram:BotUsername"]?.TrimStart('@');
            return link with { Url = string.IsNullOrWhiteSpace(username) ? null : $"https://t.me/{Uri.EscapeDataString(username)}?start={link.Code}" };
        }).RequireAuthorization();
        app.MapDelete("/api/me/telegram-link", async (ClaimsPrincipal user, TelegramStudents students, CancellationToken ct) =>
        { await students.UnlinkAsync(Guid.Parse(user.FindFirstValue("sub")!), ct); return TypedResults.NoContent(); }).RequireAuthorization();

        var bot = app.MapGroup("/internal/telegram/{telegramId:long}").AddEndpointFilter(async (context, next) =>
        {
            var expected = app.Configuration["Telegram:InternalKey"];
            var supplied = context.HttpContext.Request.Headers["X-Internal-Key"].ToString();
            if (string.IsNullOrWhiteSpace(expected) || !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(supplied))))
                return Results.Problem(statusCode: 401, title: "Invalid bot credentials.");
            return await next(context);
        });
        bot.MapPost("/link", async (long telegramId, CompleteTelegramLinkRequest request, TelegramStudents students, CancellationToken ct) =>
        { await students.LinkAsync(telegramId, request.Code, ct); return TypedResults.NoContent(); });
        bot.MapGroup("/learning").MapLevelRoutes((context, ct) =>
            context.RequestServices.GetRequiredService<TelegramStudents>().ResolveAsync(
                long.Parse(context.Request.RouteValues["telegramId"]!.ToString()!), ct));
        bot.MapGet("/profile", async (long telegramId, TelegramStudents students, IndependentAccounts accounts, CoreDbContext db, CancellationToken ct) =>
        {
            var profile = await accounts.ProfileAsync(await students.ResolveAsync(telegramId, ct), ct);
            var school = await db.Schools.Where(x => x.Id == profile.SchoolId).Select(x => x.Name).SingleOrDefaultAsync(ct);
            var studyClass = await db.Classes.Where(x => x.Id == profile.ClassId).Select(x => x.Name).SingleOrDefaultAsync(ct);
            return new TelegramProfileResponse(profile.Id, profile.Username, profile.SchoolId, profile.ClassId, school, studyClass);
        });
        bot.MapPost("/reviews/next", async (long telegramId, string? kind, TelegramStudents students, IReviewWorkflow reviews, CancellationToken ct) =>
        {
            var assignment = await reviews.ReceiveAsync(await students.ResolveAsync(telegramId, ct), kind ?? "translation-choice", ct);
            return assignment is null ? Results.NoContent() : Results.Ok(assignment);
        }).Produces<AssignmentResponse>().Produces(StatusCodes.Status204NoContent);
        bot.MapPost("/reviews/{assignmentId:guid}/response", async (long telegramId, Guid assignmentId, SubmitReviewRequest request,
            TelegramStudents students, IReviewWorkflow reviews, CancellationToken ct) =>
            await reviews.SubmitAsync(await students.ResolveAsync(telegramId, ct), assignmentId, request, ct));
    }
}

/// <summary>Raw signed launch data supplied by Telegram.WebApp.initData.</summary>
public sealed record TelegramMiniAppSignInRequest(string InitData);
