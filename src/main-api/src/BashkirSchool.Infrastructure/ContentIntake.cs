using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BashkirSchool.Application;
using BashkirSchool.Domain;
using Microsoft.EntityFrameworkCore;

namespace BashkirSchool.Infrastructure;

public sealed class ContentIntake(CoreDbContext db, IPermissionChecker permissions, TimeProvider clock) : IContentIntake
{
    public async Task<DeliveryResponse> DeliverAsync(Guid producer, string key, DeliverContentRequest request, CancellationToken ct)
    {
        Guard.Text(key, nameof(key), 200);
        if (request.Source is null) throw new DomainException("Source is required.");
        Guard.Text(request.Source.DocumentId, "source.documentId", 200);
        if (request.Source.StartSeconds < 0 || request.Source.EndSeconds < 0 ||
            request.Source.StartSeconds.HasValue != request.Source.EndSeconds.HasValue ||
            request.Source.EndSeconds <= request.Source.StartSeconds)
            throw new DomainException("Invalid source interval.");
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        if (!await permissions.HasAsync(producer, "content.deliver", AccessScope.Own, producer, ct)) throw new AccessDeniedException();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
        var previous = await db.Deliveries.SingleOrDefaultAsync(x => x.ProducerId == producer && x.Key == key, ct);
        if (previous is not null)
        {
            if (previous.PayloadHash != hash) throw new ConflictException("Idempotency key was used with different content.");
            var existing = await db.Cases.SingleAsync(x => x.Id == previous.CaseId, ct);
            return new(existing.Id, existing.FragmentId, existing.ShownVariantId);
        }
        if (await db.Fragments.AnyAsync(x => x.ProducerId == producer && x.ExternalId == request.FragmentId && x.LanguagePair == request.LangPair, ct))
            throw new ConflictException("External fragment already exists; revision intake is not enabled.");
        var fragment = Fragment.Create(producer, request.FragmentId, request.TextBa, request.LangPair, JsonSerializer.Serialize(request.Source));
        // IDs are assigned before persistence so the whole intake is one transaction.
        var translationCase = TranslationCase.Open(fragment.Id, Guid.NewGuid(), clock.GetUtcNow());
        var variant = TranslationVariant.Create(translationCase.Id, request.Translation, true, translationCase.ShownVariantId);
        db.AddRange(fragment, translationCase, variant, new ContentDelivery { ProducerId = producer, Key = key, PayloadHash = hash, CaseId = translationCase.Id });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(translationCase.Id, fragment.Id, variant.Id);
    }
}
