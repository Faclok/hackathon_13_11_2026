using System.Security.Claims;
using BashkirSchool.Domain;
using BashkirSchool.Infrastructure;
using Microsoft.EntityFrameworkCore;

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
            var result = new List<object>(cases.Length);
            foreach (var item in cases)
            {
                var fragment = await db.Fragments.AsNoTracking().SingleAsync(x => x.Id == item.FragmentId, ct);
                var resolution = item.ResolutionId is Guid id
                    ? await db.Resolutions.AsNoTracking().SingleAsync(x => x.Id == id, ct) : null;
                var variantId = resolution?.PrimaryVariantId ?? item.ShownVariantId;
                var variant = await db.Variants.AsNoTracking().SingleAsync(x => x.Id == variantId, ct);
                var rating = ReviewStatisticsCalculator.Calculate(await evidence.OpinionsAsync(item.Id, ct), [variantId]).Variants[0].Supports;
                result.Add(new { id = item.Id, sourceText = fragment.Text, translationText = variant.Text,
                    languagePair = fragment.LanguagePair, rating,
                    status = item.NeedsExpert ? "expert_review" : resolution?.PrimaryVariantId is not null ? "confirmed" : "pending" });
            }
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
            if (item is null) return Results.NotFound();
            var fragment = await db.Fragments.AsNoTracking().SingleAsync(x => x.Id == item.FragmentId, ct);
            var resolution = item.ResolutionId is Guid id
                ? await db.Resolutions.AsNoTracking().SingleAsync(x => x.Id == id, ct) : null;
            var variantId = resolution?.PrimaryVariantId ?? item.ShownVariantId;
            var variant = await db.Variants.AsNoTracking().SingleAsync(x => x.Id == variantId, ct);
            var rating = ReviewStatisticsCalculator.Calculate(await evidence.OpinionsAsync(item.Id, ct), [variantId]).Variants[0].Supports;
            return Results.Ok(new { id = item.Id, sourceText = fragment.Text, translationText = variant.Text,
                languagePair = fragment.LanguagePair, rating,
                status = item.NeedsExpert ? "expert_review" : resolution?.PrimaryVariantId is not null ? "confirmed" : "pending" });
        });
    }
}
