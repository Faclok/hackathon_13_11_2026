using BashkirSchool.Domain;
using BashkirSchool.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace BashkirSchool.Host;

public static class LmsCatalogEndpoints
{
    public static void MapLmsCatalogEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/lms-source").RequireAuthorization();
        api.MapGet("/translations", async (int? offset, int? limit, CoreDbContext db, CaseEvidence evidence, CancellationToken ct) =>
        {
            var skip = offset ?? 0;
            var take = limit ?? 50;
            if (skip < 0 || take is < 1 or > 100) return Results.BadRequest(new { title = "Invalid pagination." });
            var cases = await db.Cases.AsNoTracking().OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
                .Skip(skip).Take(take).ToArrayAsync(ct);
            var result = new List<CatalogRow>(cases.Length);
            foreach (var item in cases) result.Add(await ReadTranslation(item, db, evidence, ct));
            return Results.Ok(result);
        });
        api.MapGet("/participants/lookup", async (string username, CoreDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(username)) return Results.BadRequest(new { title = "Username is required." });
            var user = await db.Users.AsNoTracking().Where(x => x.NormalizedUserName == username.Trim().ToUpperInvariant() && x.Active)
                .Select(x => new { x.Id, x.UserName }).SingleOrDefaultAsync(ct);
            return user is not null && await db.Participants.AnyAsync(x => x.Id == user.Id, ct)
                ? Results.Ok(new { id = user.Id, username = user.UserName }) : Results.NotFound();
        });
        api.MapGet("/translations/{caseId:guid}", async (Guid caseId, CoreDbContext db, CaseEvidence evidence, CancellationToken ct) =>
        {
            var item = await db.Cases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == caseId, ct);
            return item is null ? Results.NotFound() : Results.Ok(await ReadTranslation(item, db, evidence, ct));
        });
        api.MapPost("/teacher/translations", async (TeacherCandidateInput input, ClaimsPrincipal principal,
            HttpRequest request, IConfiguration configuration, CoreDbContext db, CancellationToken ct) =>
        {
            if (!LmsAuthorized(request, configuration)) return Results.Unauthorized();
            var actor = Guid.Parse(principal.FindFirstValue("sub")!);
            if (input.ExternalId == Guid.Empty || string.IsNullOrWhiteSpace(input.SourceText) ||
                string.IsNullOrWhiteSpace(input.TranslationText) || input.SourceText.Length > 5000 ||
                input.TranslationText.Length > 5000 || input.LanguagePair is not ("ba-ru" or "ru-ba"))
                return Results.BadRequest(new { title = "Invalid translation candidate." });
            var externalId = $"lms-{input.ExternalId}";
            var previous = await db.Fragments.AsNoTracking().SingleOrDefaultAsync(x => x.ProducerId == actor && x.ExternalId == externalId && x.LanguagePair == input.LanguagePair, ct);
            if (previous is not null)
            {
                var existingCase = await db.Cases.AsNoTracking().SingleAsync(x => x.FragmentId == previous.Id, ct);
                var variant = await db.Variants.AsNoTracking().SingleAsync(x => x.Id == existingCase.ShownVariantId, ct);
                return previous.Text == input.SourceText.Trim() && variant.Text == input.TranslationText.Trim()
                    ? Results.Ok(new { caseId = existingCase.Id })
                    : Results.Conflict(new { title = "External ID already belongs to another translation." });
            }
            var fragment = Fragment.Create(actor, externalId, input.SourceText, input.LanguagePair,
                JsonSerializer.Serialize(new { document_id = externalId, title = "Материал преподавателя" }));
            var translationCase = TranslationCase.Open(fragment.Id, Guid.NewGuid(), DateTimeOffset.UtcNow);
            var candidate = TranslationVariant.Create(translationCase.Id, input.TranslationText, true, translationCase.ShownVariantId);
            db.AddRange(fragment, translationCase, candidate);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { caseId = translationCase.Id });
        });
        api.MapPost("/translations/{caseId:guid}/expert-request", async (Guid caseId, ExpertRequestInput input,
            ClaimsPrincipal principal, HttpRequest request, IConfiguration configuration, CoreDbContext db, CancellationToken ct) =>
        {
            if (!LmsAuthorized(request, configuration)) return Results.Unauthorized();
            var actor = Guid.Parse(principal.FindFirstValue("sub")!);
            if (string.IsNullOrWhiteSpace(input.Note) || input.Note.Trim().Length > 1000)
                return Results.BadRequest(new { title = "A note of up to 1000 characters is required." });
            var translationCase = await db.Cases.SingleOrDefaultAsync(x => x.Id == caseId, ct);
            if (translationCase is null) return Results.NotFound();
            var owner = await db.Fragments.AsNoTracking().AnyAsync(x => x.Id == translationCase.FragmentId && x.ProducerId == actor && x.ExternalId.StartsWith("lms-"), ct);
            if (!owner) return Results.Forbid();
            if (translationCase.ExpertClosed) return Results.Conflict(new { title = "Expert decision already exists." });
            if (await db.Audit.AnyAsync(x => x.TargetId == caseId && x.Action == "lms.expert.requested", ct))
                return Results.Conflict(new { title = "Expert review already requested." });
            db.Audit.Add(new() { ActorId = actor, TargetId = caseId, Action = "lms.expert.requested",
                Reason = input.Note.Trim(), At = DateTimeOffset.UtcNow });
            translationCase.SetDisputed(true);
            await db.SaveChangesAsync(ct);
            return Results.Accepted($"/api/lms-source/translations/{caseId}");
        });
    }

    private static async Task<CatalogRow> ReadTranslation(TranslationCase item, CoreDbContext db, CaseEvidence evidence, CancellationToken ct)
    {
        var fragment = await db.Fragments.AsNoTracking().SingleAsync(x => x.Id == item.FragmentId, ct);
        var resolution = item.ResolutionId is Guid id
            ? await db.Resolutions.AsNoTracking().SingleAsync(x => x.Id == id, ct) : null;
        var variants = await db.Variants.AsNoTracking().Where(x => x.CaseId == item.Id).ToArrayAsync(ct);
        var statistics = ReviewStatisticsCalculator.Calculate(await evidence.OpinionsAsync(item.Id, ct), variants.Select(x => x.Id));
        var selectedId = resolution?.PrimaryVariantId ?? statistics.Variants
            .OrderByDescending(x => x.Supports).ThenByDescending(x => x.VariantId == item.ShownVariantId)
            .ThenBy(x => x.VariantId).First().VariantId;
        var selected = variants.Single(x => x.Id == selectedId);
        var rating = statistics.Variants.Single(x => x.VariantId == selectedId).Supports;
        var status = selected.Text == "" ? "untranslated" : resolution?.Kind == ResolutionKind.Expert ? "expert" : item.NeedsExpert ? "expert_review" :
            resolution?.Kind == ResolutionKind.Community ? "community" : "pending";
        var accepted = resolution?.Accepted.Select(id => variants.Single(x => x.Id == id).Text).ToArray() ?? [];
        var requested = await db.Audit.AsNoTracking().AnyAsync(x => x.TargetId == item.Id && x.Action == "lms.expert.requested", ct);
        return new(item.Id, fragment.Text, selected.Text, fragment.LanguagePair, rating, status, accepted, requested);
    }

    private sealed record CatalogRow(Guid Id, string SourceText, string TranslationText, string LanguagePair, int Rating,
        string Status, string[] AcceptedTranslations, bool ExpertRequested);
    private sealed record TeacherCandidateInput(Guid ExternalId, string SourceText, string TranslationText, string LanguagePair);
    private sealed record ExpertRequestInput(string Note);
    private static bool LmsAuthorized(HttpRequest request, IConfiguration configuration)
    {
        var expected = configuration["Lms:InternalKey"];
        var supplied = request.Headers["X-Internal-Key"].ToString();
        return !string.IsNullOrWhiteSpace(expected) && CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(supplied)));
    }
}
