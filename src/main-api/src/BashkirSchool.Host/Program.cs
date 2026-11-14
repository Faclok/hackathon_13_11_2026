using System.Security.Claims;
using System.Text.Json.Serialization;
using BashkirSchool.Application;
using BashkirSchool.Domain;
using BashkirSchool.Infrastructure;
using BashkirSchool.Host;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.OpenApi;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCoreInfrastructure(builder.Configuration);
builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, context, ct) =>
{
    document.Info.Title = "Bashkir School Core";
    document.Servers = [new OpenApiServer { Url = "/" }];
    document.Components ??= new();
    document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
    document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" };
    foreach (var path in document.Paths.Where(x => x.Key.StartsWith("/api/", StringComparison.Ordinal) &&
        !x.Key.StartsWith("/api/public/", StringComparison.Ordinal)))
        foreach (var operation in path.Value.Operations!.Values)
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] });
            operation.Responses ??= new();
            operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Authentication required or session revoked." });
            operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Permission or resource scope denied." });
        }
    return Task.CompletedTask;
}));
builder.Services.AddProblemDetails();
var registrationLimit = builder.Configuration.GetValue("Registration:PermitLimit", 10);
var registrationWindow = builder.Configuration.GetValue("Registration:WindowSeconds", 60);
if (registrationLimit < 1 || registrationWindow < 1) throw new InvalidOperationException("Registration rate limit and window must be positive.");
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("registration", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = registrationLimit, Window = TimeSpan.FromSeconds(registrationWindow), QueueLimit = 0, AutoReplenishment = true }));
    options.AddPolicy("public-tts", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
});
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
    o.SerializerOptions.AllowDuplicateProperties = false;
    o.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
});
var app = builder.Build();

if (args.Contains("--migrate") || args.Contains("--bootstrap-admin"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
    await db.Database.MigrateAsync();
    if (args.Contains("--bootstrap-admin"))
        await scope.ServiceProvider.GetRequiredService<SchoolAccounts>().BootstrapAsync(
            builder.Configuration["Bootstrap:Username"] ?? throw new InvalidOperationException("Bootstrap:Username is required."),
            builder.Configuration["Bootstrap:Password"] ?? throw new InvalidOperationException("Bootstrap:Password is required."), default);
    return;
}

await DatabaseStartup.InitializeAsync(app.Services, app.Configuration);

app.UseExceptionHandler(error => error.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()!.Error;
    var status = exception switch
    {
        AccessDeniedException => context.User.Identity?.IsAuthenticated == true ? 403 : 401,
        MissingResourceException => 404,
        DomainException => 400,
        BadHttpRequestException => 400,
        ConflictException or DbUpdateConcurrencyException => 409,
        _ when DatabaseConflicts.IsConflict(exception) => 409,
        _ => 500
    };
    await Results.Problem(statusCode: status, title: status switch
    {
        400 => exception is DomainException ? exception.Message : "Invalid request body.", 401 => "Invalid credentials.", 403 => "Access denied.",
        404 => "Resource not found.", 409 => "Conflict; reread state or retry the identical request.", _ => "An unexpected error occurred."
    }).ExecuteAsync(context);
}));
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
app.MapGet("/health/ready", async (CoreDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503));

app.MapPost("/auth/sign-in", (SignInRequest request, SchoolAccounts accounts, CancellationToken ct) => accounts.SignInAsync(request, ct));
app.MapPost("/auth/username", (UsernameLoginRequest request, IndependentAccounts accounts, CancellationToken ct) =>
    accounts.UsernameLoginAsync(request.Username, ct)).RequireRateLimiting("registration")
    .ProducesProblem(StatusCodes.Status429TooManyRequests);
app.MapPost("/auth/register", async (RegisterAccountRequest request, IndependentAccounts accounts, CancellationToken ct) =>
    TypedResults.Created("/api/me", await accounts.RegisterAsync(request, ct))).RequireRateLimiting("registration").ProducesProblem(StatusCodes.Status429TooManyRequests);
app.MapPost("/auth/refresh", (RefreshSessionRequest request, Sessions sessions, CancellationToken ct) => sessions.RefreshAsync(request.RefreshToken, ct));
app.MapPost("/auth/password", async (CompletePasswordRequest request, SchoolAccounts accounts, CancellationToken ct) =>
{ await accounts.CompletePasswordAsync(request, ct); return TypedResults.NoContent(); });

var api = app.MapGroup("/api").RequireAuthorization();
api.MapGet("/me", (ClaimsPrincipal user, IndependentAccounts accounts, CancellationToken ct) => accounts.ProfileAsync(Subject(user), ct));
api.MapPut("/me", (ClaimsPrincipal user, UpdateMyProfileRequest request, IndependentAccounts accounts, CancellationToken ct) =>
    accounts.UpdateProfileAsync(Subject(user), request, ct));
api.MapGet("/player/profile", (ClaimsPrincipal user, PlayerEconomy economy, CancellationToken ct) =>
    economy.ProfileAsync(Subject(user), ct));
api.MapGet("/player/decorations", () => Results.Ok(RewardCatalog.Items));
api.MapGet("/player/cases", () => Results.Ok(RewardCatalog.Cases.Select(RewardCatalog.Describe)));
api.MapGet("/player/cases/history", (ClaimsPrincipal user, PlayerEconomy economy, CancellationToken ct) =>
    economy.HistoryAsync(Subject(user), ct));
api.MapPost("/player/keys", (ClaimsPrincipal user, BuyCaseKeysRequest request, PlayerEconomy economy, CancellationToken ct) =>
    economy.BuyKeysAsync(Subject(user), request.Quantity, ct));
api.MapPost("/player/cases/{caseId}/open", (ClaimsPrincipal user, string caseId, PlayerEconomy economy, CancellationToken ct) =>
    economy.OpenCaseAsync(Subject(user), caseId, ct));
api.MapPut("/player/equipment/{itemId}", (ClaimsPrincipal user, string itemId, EquipDecorationRequest request, PlayerEconomy economy, CancellationToken ct) =>
    economy.EquipAsync(Subject(user), itemId, request.Equipped, ct));
api.MapGet("/leaderboard", (ClaimsPrincipal user, string? period, string? metric, PlayerEconomy economy, CancellationToken ct) =>
    economy.LeaderboardAsync(Subject(user), period ?? "global", metric ?? "tasks", ct));
api.MapPost("/accounts/{accountId:guid}/recovery-link", (ClaimsPrincipal user, Guid accountId, RecoverIndependentAccountRequest request,
    IndependentAccounts accounts, CancellationToken ct) => accounts.IssueRecoveryAsync(Subject(user), accountId, request, ct));
api.MapPost("/credentials/change", async (ClaimsPrincipal user, ChangePasswordRequest request, SchoolAccounts accounts, CancellationToken ct) =>
{ await accounts.ChangePasswordAsync(Subject(user), request, ct); return TypedResults.NoContent(); });
api.MapGet("/sessions", (ClaimsPrincipal user, Sessions sessions, CancellationToken ct) => sessions.ListAsync(Subject(user), ct));
api.MapDelete("/sessions/{sessionId:guid}", async (ClaimsPrincipal user, Guid sessionId, Sessions sessions, CancellationToken ct) =>
{ await sessions.RevokeAsync(Subject(user), sessionId, ct); return TypedResults.NoContent(); });
api.MapGet("/schools", (ClaimsPrincipal user, int? offset, int? limit, SchoolDirectory directory, CancellationToken ct) => directory.SchoolsAsync(Subject(user), offset ?? 0, limit ?? 50, ct));
api.MapGet("/classes", (ClaimsPrincipal user, int? offset, int? limit, SchoolDirectory directory, CancellationToken ct) => directory.ClassesAsync(Subject(user), offset ?? 0, limit ?? 50, ct));
api.MapGet("/classes/{classId:guid}/students", (ClaimsPrincipal user, Guid classId, int? offset, int? limit, SchoolDirectory directory, CancellationToken ct) => directory.StudentsAsync(Subject(user), classId, offset ?? 0, limit ?? 50, ct));
api.MapPut("/accounts/{accountId:guid}/access", async (ClaimsPrincipal user, Guid accountId, AssignAccessRequest request, SchoolDirectory directory, CancellationToken ct) =>
{ await directory.AssignAsync(Subject(user), accountId, request, ct); return TypedResults.NoContent(); });
api.MapDelete("/access/{assignmentId:guid}", async (ClaimsPrincipal user, Guid assignmentId, SchoolDirectory directory, CancellationToken ct) =>
{ await directory.RevokeAsync(Subject(user), assignmentId, ct); return TypedResults.NoContent(); });
api.MapGet("/expert/cases", (ClaimsPrincipal user, int? offset, int? limit, ExpertReviews expert, CancellationToken ct) => expert.QueueAsync(Subject(user), offset ?? 0, limit ?? 50, ct));
api.MapGet("/expert/dashboard", (ClaimsPrincipal user, ExpertReviews expert, CancellationToken ct) => expert.DashboardAsync(Subject(user), ct));
api.MapGet("/expert/work", (ClaimsPrincipal user, ExpertReviews expert, CancellationToken ct) => expert.WorkAsync(Subject(user), ct));
api.MapGet("/expert/preferences", (ClaimsPrincipal user, ExpertReviews expert, CancellationToken ct) => expert.PreferencesAsync(Subject(user), ct));
api.MapPut("/expert/preferences", (ClaimsPrincipal user, ExpertPreferencesResponse preferences, ExpertReviews expert, CancellationToken ct) =>
    expert.SavePreferencesAsync(Subject(user), preferences, ct));
api.MapPost("/expert/cases/{caseId:guid}/claim", (ClaimsPrincipal user, Guid caseId, ExpertReviews expert, CancellationToken ct) => expert.ClaimAsync(Subject(user), caseId, ct));
api.MapDelete("/expert/cases/{caseId:guid}/claim", async (ClaimsPrincipal user, Guid caseId, ExpertReviews expert, CancellationToken ct) =>
{ await expert.ReleaseAsync(Subject(user), caseId, ct); return TypedResults.NoContent(); });
api.MapGet("/expert/cases/{caseId:guid}", (ClaimsPrincipal user, Guid caseId, ExpertReviews expert, CancellationToken ct) => expert.ReadAsync(Subject(user), caseId, ct));
api.MapGet("/expert/cases/{caseId:guid}/history", (ClaimsPrincipal user, Guid caseId, ExpertReviews expert, CancellationToken ct) => expert.HistoryAsync(Subject(user), caseId, ct));
api.MapPost("/expert/cases/{caseId:guid}/variants", async (ClaimsPrincipal user, Guid caseId, ProposeTranslationRequest request, ExpertReviews expert, CancellationToken ct) =>
    TypedResults.Ok(new ResourceCreatedResponse(await expert.ProposeAsync(Subject(user), caseId, request, ct))));
api.MapGet("/accounts/{accountId:guid}/access", (ClaimsPrincipal user, Guid accountId, SchoolDirectory directory, CancellationToken ct) => directory.AccessAsync(Subject(user), accountId, ct));
api.MapPost("/expert/cases/{caseId:guid}/resolution", (ClaimsPrincipal user, Guid caseId, ResolveCaseRequest request, ExpertReviews expert, CancellationToken ct) => expert.ResolveAsync(Subject(user), caseId, request, ct));
api.MapGet("/translations/export", (ClaimsPrincipal user, int? offset, int? limit, bool? includeDisputed, TranslationExport export, CancellationToken ct) => export.ReadAsync(Subject(user), offset ?? 0, limit ?? 100, includeDisputed ?? false, ct));
api.MapGet("/lessons/today", (ClaimsPrincipal user, LearningWorkflow learning, CancellationToken ct) => learning.TodayAsync(Subject(user), ct));
api.MapPost("/lessons/today/boss", async (ClaimsPrincipal user, LearningWorkflow learning, IReviewWorkflow reviews, CancellationToken ct) =>
{
    var boss = await learning.StartBossAsync(Subject(user), reviews, ct);
    return boss is null ? Results.NoContent() : Results.Ok(boss);
}).Produces<AssignmentResponse>().Produces(StatusCodes.Status204NoContent);
api.MapPost("/exercises/{exerciseId:guid}/attempt", (ClaimsPrincipal user, Guid exerciseId, SubmitAttemptRequest request, LearningWorkflow learning, CancellationToken ct) => learning.SubmitAsync(Subject(user), exerciseId, request, ct));
api.MapGet("/exercises/kinds", (ExerciseRegistry registry) => registry.Kinds)
    .WithName("ListExerciseKinds").WithSummary("Supported learning exercise kinds and answer schema versions.")
    .Produces<IReadOnlyList<ExerciseKindResponse>>();
api.MapPost("/lessons/today/exercises", async (ClaimsPrincipal user, IssueExerciseRequest request, LearningWorkflow learning, CancellationToken ct) =>
{
    var exercise = await learning.IssueAsync(Subject(user), request, ct);
    return exercise is null ? Results.NoContent() : Results.Ok(exercise);
}).WithName("IssueExercise").WithSummary("Issue or resume today's exercise of a requested kind; audio is prepared lazily.")
    .Produces<ExerciseResponse>().Produces(StatusCodes.Status204NoContent).ProducesProblem(400).ProducesProblem(409);
api.MapPost("/exercises/{exerciseId:guid}/answer", (ClaimsPrincipal user, Guid exerciseId, SubmitExerciseAnswerRequest request, LearningWorkflow learning, CancellationToken ct) =>
    learning.AnswerAsync(Subject(user), exerciseId, request, ct))
    .WithName("SubmitExerciseAnswer").WithSummary("Submit a kind-specific answer without exposing the private answer key.")
    .Produces<AttemptResponse>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);
api.MapPost("/vocabulary", (ClaimsPrincipal user, DeliverVocabularyRequest request, LearningMaterials materials, CancellationToken ct) =>
    materials.DeliverAsync(Subject(user), request, ct))
    .WithName("DeliverVocabulary").WithSummary("Idempotently import prepared word pairs as an authorized content producer.")
    .Produces<VocabularyDeliveryResponse>().ProducesProblem(400);
api.MapPost("/cases/{caseId:guid}/audio", (ClaimsPrincipal user, Guid caseId, AudioPreparation audio, CancellationToken ct) =>
    audio.PrepareAsync(Subject(user), caseId, ct))
    .WithName("PrepareSentenceAudio").WithSummary("Create and cache Silero audio in object storage, reusing existing recordings.")
    .Produces<AudioAssetResponse>().ProducesProblem(404);
api.MapPost("/schools", async (ClaimsPrincipal user, CreateSchoolRequest request, SchoolAccounts accounts, CancellationToken ct) =>
    TypedResults.Created("/api/schools", new ResourceCreatedResponse(await accounts.CreateSchoolAsync(Subject(user), request, ct))));
api.MapPost("/staff", async (ClaimsPrincipal user, CreateStaffRequest request, SchoolAccounts accounts, CancellationToken ct) =>
    TypedResults.Created("/api/staff", await accounts.CreateStaffAsync(Subject(user), request, ct)));
api.MapPost("/classes", async (ClaimsPrincipal user, CreateClassRequest request, SchoolAccounts accounts, CancellationToken ct) =>
    TypedResults.Created("/api/classes", new ResourceCreatedResponse(await accounts.CreateClassAsync(Subject(user), request, ct))));
api.MapPost("/classes/{classId:guid}/students", async (ClaimsPrincipal user, Guid classId, CreateStudentRequest request, SchoolAccounts accounts, CancellationToken ct) =>
    TypedResults.Ok(await accounts.CreateStudentAsync(Subject(user), classId, request, ct)));
api.MapPost("/accounts/{accountId:guid}/password-link", (ClaimsPrincipal user, Guid accountId, SchoolAccounts accounts, CancellationToken ct) =>
    accounts.IssuePasswordAsync(Subject(user), accountId, ct));
api.MapPut("/accounts/{accountId:guid}/status", async (ClaimsPrincipal user, Guid accountId, AccountStatusRequest request, SchoolAccounts accounts, CancellationToken ct) =>
{ await accounts.SetActiveAsync(Subject(user), accountId, request.Active, ct); return TypedResults.NoContent(); });
api.MapPost("/policies/distribution", async (ClaimsPrincipal user, DistributionPolicyRequest request, SchoolAccounts accounts, CancellationToken ct) =>
    TypedResults.Created("/api/policies/distribution", new ResourceCreatedResponse(await accounts.SavePolicyAsync(Subject(user), request.ToPolicy(), ct))));
api.MapGet("/policies/distribution", async (ClaimsPrincipal user, IPermissionChecker permissions, CoreDbContext db, CancellationToken ct) =>
{
    if (!await permissions.HasAsync(Subject(user), "policies.distribution.manage", AccessScope.Global, null, ct) ||
        !await permissions.HasAsync(Subject(user), "policies.consensus.manage", AccessScope.Global, null, ct)) throw new AccessDeniedException();
    var p = await db.Policies.AsNoTracking().OrderByDescending(x => x.Version).FirstAsync(ct);
    return new DistributionPolicyResponse(p.Id, p.Version, new(p.RequiredParticipants, p.SupportThreshold, p.MinimumSchools,
        p.MinimumClasses, p.MaximumSchoolShare, p.MaximumClassShare, p.AssignmentMinutes, p.RepeatHours, p.WorkingSetSize));
});
api.MapPost("/deliveries", async (ClaimsPrincipal user, [FromHeader(Name = "Idempotency-Key")] string key,
    DeliverContentRequest request, IContentIntake intake, CancellationToken ct) =>
    TypedResults.Ok(await intake.DeliverAsync(Subject(user), key, request, ct)));
api.MapPost("/expert/imports", (ClaimsPrincipal user, ExpertDatasetRow request, ExpertDatasetIntake intake, CancellationToken ct) =>
    intake.ImportAsync(Subject(user), request, ct));
api.MapGet("/expert/untranslated", (ClaimsPrincipal user, int? offset, int? limit, ExpertDatasetIntake intake, CancellationToken ct) =>
    intake.UntranslatedAsync(Subject(user), offset ?? 0, limit ?? 100, ct));
api.MapPost("/expert/fragments/{fragmentId:guid}/translation", (ClaimsPrincipal user, Guid fragmentId,
    VerifiedFragmentTranslation request, ExpertDatasetIntake intake, CancellationToken ct) =>
    intake.TranslateAsync(Subject(user), fragmentId, request, ct));
api.MapPost("/reviews/next", async (ClaimsPrincipal user, string? kind, IReviewWorkflow reviews, CancellationToken ct) =>
{
    var assignment = await reviews.ReceiveAsync(Subject(user), kind ?? "translation-confirm", ct);
    return assignment is null ? Results.NoContent() : Results.Ok(assignment);
}).Produces<AssignmentResponse>().Produces(StatusCodes.Status204NoContent);
api.MapGet("/boss/access", (ClaimsPrincipal user, BossAccess access, CancellationToken ct) =>
    access.ReadAsync(Subject(user), ct));
api.MapPost("/boss/next", async (ClaimsPrincipal user, BossAccess access, IReviewWorkflow reviews, CancellationToken ct) =>
{
    if (!(await access.ReadAsync(Subject(user), ct)).CanAccess) return Results.Forbid();
    var assignment = await reviews.ReceiveBossAsync(Subject(user), ct);
    return assignment is null ? Results.NoContent() : Results.Ok(assignment);
}).Produces<AssignmentResponse>().Produces(StatusCodes.Status204NoContent);
api.MapPost("/reviews/{assignmentId:guid}/response", (ClaimsPrincipal user, Guid assignmentId, SubmitReviewRequest request, IReviewWorkflow reviews, CancellationToken ct) =>
    reviews.SubmitAsync(Subject(user), assignmentId, request, ct));
api.MapGet("/cases/{caseId:guid}/statistics", (ClaimsPrincipal user, Guid caseId, IReviewWorkflow reviews, CancellationToken ct) =>
    reviews.StatisticsAsync(Subject(user), caseId, ct));

app.MapTelegramEndpoints();
app.MapClassInvitationEndpoints();
app.MapLmsCatalogEndpoints();
app.MapVerifiedTaskEndpoints();
app.Run();
static Guid Subject(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue("sub")!);
public partial class Program;

/// <summary>Changes account availability and revokes previously issued sessions.</summary>
public sealed record AccountStatusRequest(bool Active);
/// <summary>The identifier of a newly created resource.</summary>
public sealed record ResourceCreatedResponse(Guid Id);
public sealed record UsernameLoginRequest(string Username);
public sealed record BuyCaseKeysRequest(int Quantity);
public sealed record EquipDecorationRequest(bool Equipped);
/// <summary>The currently active version of administrator settings.</summary>
public sealed record DistributionPolicyResponse(Guid Id, int Version, DistributionPolicyRequest Settings);
/// <summary>Creates a new immutable version of the distribution and consensus settings.</summary>
public sealed record DistributionPolicyRequest(int RequiredParticipants = 300, decimal SupportThreshold = .51m,
    int MinimumSchools = 2, int MinimumClasses = 3, decimal MaximumSchoolShare = .6m, decimal MaximumClassShare = .4m,
    int AssignmentMinutes = 30, int RepeatHours = 24, int WorkingSetSize = 20)
{
    public DistributionPolicy ToPolicy() => new() { RequiredParticipants = RequiredParticipants, SupportThreshold = SupportThreshold,
        MinimumSchools = MinimumSchools, MinimumClasses = MinimumClasses, MaximumSchoolShare = MaximumSchoolShare,
        MaximumClassShare = MaximumClassShare, AssignmentMinutes = AssignmentMinutes, RepeatHours = RepeatHours, WorkingSetSize = WorkingSetSize };
}
