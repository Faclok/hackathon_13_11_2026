using System.Security.Claims;
using BashkirSchool.Application;
using BashkirSchool.Infrastructure;

namespace BashkirSchool.Host;

public static class LevelEndpoints
{
    public static void MapLevelEndpoints(this WebApplication app) =>
        app.MapGroup("/api/learning").RequireAuthorization().MapLevelRoutes(
            (context, _) => Task.FromResult(Guid.Parse(context.User.FindFirstValue("sub")!)));

    public static void MapLevelRoutes(this RouteGroupBuilder group, Func<HttpContext, CancellationToken, Task<Guid>> actor)
    {
        group.MapPost("/current", async (HttpContext context, LevelWorkflow levels, CancellationToken ct) =>
        {
            var level = await levels.CurrentAsync(await actor(context, ct), ct);
            return level is null ? Results.NoContent() : Results.Ok(level);
        }).WithSummary("Issue or resume the current personal level, exposing only its current question.")
            .Produces<LevelResponse>().Produces(StatusCodes.Status204NoContent).ProducesProblem(409);
        group.MapPost("/levels/{levelId:guid}/questions/{questionId:guid}/answer", async (HttpContext context,
            Guid levelId, Guid questionId, AnswerLevelRequest request, LevelWorkflow levels, CancellationToken ct) =>
            await levels.AnswerAsync(await actor(context, ct), levelId, questionId, request, ct))
            .WithSummary("Answer the current question; wrong answers return to the end of the queue.")
            .Produces<LevelAnswerResponse>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);
        group.MapPost("/levels/{levelId:guid}/questions/{questionId:guid}/pairs/check", async (HttpContext context,
            Guid levelId, Guid questionId, CheckWordPairRequest request, LevelWorkflow levels, CancellationToken ct) =>
            await levels.CheckWordPairAsync(await actor(context, ct), levelId, questionId, request, ct))
            .WithSummary("Check one word pair without submitting the whole exercise.")
            .Produces<CheckWordPairResponse>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);
        group.MapGet("/progress", async (HttpContext context, LevelWorkflow levels, CancellationToken ct) =>
            await levels.ProgressAsync(await actor(context, ct), ct)).Produces<LevelProgressResponse>();
        group.MapGet("/statistics", async (HttpContext context, int? offset, int? limit, LevelWorkflow levels, CancellationToken ct) =>
            await levels.StatisticsAsync(await actor(context, ct), offset ?? 0, limit ?? 20, ct))
            .Produces<IReadOnlyList<LevelStatisticsResponse>>().ProducesProblem(400);
        group.MapPost("/boss", async (HttpContext context, LevelWorkflow levels, CancellationToken ct) =>
        {
            var boss = await levels.BossAsync(await actor(context, ct), ct);
            return boss is null ? Results.NoContent() : Results.Ok(boss);
        }).WithSummary("Start or resume an optional three-translation boss earned every five completed levels.")
            .Produces<LevelBossResponse>().Produces(StatusCodes.Status204NoContent).ProducesProblem(409);
        group.MapPost("/boss/start", async (HttpContext context, LevelWorkflow levels, CancellationToken ct) =>
        {
            var boss = await levels.StartBossAsync(await actor(context, ct), ct);
            if (boss is null) return Results.NoContent();
            return Results.Ok(new { boss.Id, boss.Checkpoint });
        });
        group.MapPost("/boss/teacher-answers/{assignmentId:guid}", async (HttpContext context,
            Guid assignmentId, LevelWorkflow levels, CancellationToken ct) =>
            await levels.AttachTeacherAnswerAsync(await actor(context, ct), assignmentId, ct));
        group.MapPost("/bosses/{bossId:guid}/questions/{assignmentId:guid}/answer", async (HttpContext context,
            Guid bossId, Guid assignmentId, SubmitReviewRequest request, LevelWorkflow levels, CancellationToken ct) =>
            await levels.AnswerBossAsync(await actor(context, ct), bossId, assignmentId, request, ct))
            .Produces<LevelBossAnswerResponse>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);
    }
}
