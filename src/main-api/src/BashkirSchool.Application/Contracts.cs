using BashkirSchool.Domain;
using System.Text.Json.Serialization;

namespace BashkirSchool.Application;

/// <summary>Coordinates identifying a prepared fragment in its original source.</summary>
public sealed record SourceReference([property: JsonPropertyName("document_id")] string DocumentId, string? Title = null, string? Chapter = null,
    [property: JsonPropertyName("start_seconds")] decimal? StartSeconds = null, [property: JsonPropertyName("end_seconds")] decimal? EndSeconds = null);
/// <summary>One atomic intake item; translation is an unverified machine candidate.</summary>
public sealed record DeliverContentRequest([property: JsonPropertyName("fragment_id")] string FragmentId,
    [property: JsonPropertyName("text_ba")] string TextBa, [property: JsonPropertyName("lang_pair")] string LangPair, string Translation, SourceReference Source);
/// <summary>Dataset row. A missing translation remains a source fragment without a case.</summary>
public sealed record ExpertDatasetRow([property: JsonPropertyName("fragment_id")] string FragmentId,
    [property: JsonPropertyName("text_ba")] string TextBa, [property: JsonPropertyName("lang_pair")] string LangPair,
    string? Translation, bool ExpertVerified, SourceReference Source);
public sealed record ExpertDatasetReceipt(Guid FragmentId, Guid? CaseId, Guid? VariantId, string Status);
public sealed record UntranslatedFragment(Guid FragmentId, string ExternalId, string TextBa, string LanguagePair, SourceReference Source);
public sealed record VerifiedFragmentTranslation(string Translation);
/// <summary>Identifiers created by an idempotent delivery.</summary>
public sealed record DeliveryResponse(Guid CaseId, Guid FragmentId, Guid VariantId);
/// <summary>The exact text and variant assigned to the authenticated participant.</summary>
public sealed record AssignmentResponse(Guid Id, Guid CaseId, Guid VariantId, string Original, string Translation, DateTimeOffset ExpiresAt)
{
    public string Kind { get; init; } = "translation-confirm";
    public IReadOnlyList<ReviewOptionResponse> Options { get; init; } = [];
    public bool AllowCustom => true;
}
public sealed record ReviewOptionResponse(Guid Id, string Text);
/// <summary>An opinion on the issued translation, without client supplied author or trust.</summary>
public sealed record SubmitReviewRequest([property: JsonRequired] ReviewAction Action, string? Correction = null)
{
    public Guid? SelectedVariantId { get; init; }
}
/// <summary>Receipt for a saved review.</summary>
public sealed record ReviewReceipt(Guid ResponseId, bool Archived);
/// <summary>Unique participant progress and per-variant support, without private trust data.</summary>
public sealed record CaseStatisticsResponse(int UniqueParticipants, int RequiredParticipants, decimal CompletionRatio,
    IReadOnlyList<VariantStatistics> Variants);

public interface IContentIntake
{
    Task<DeliveryResponse> DeliverAsync(Guid producer, string key, DeliverContentRequest request, CancellationToken cancellationToken);
}
public interface IReviewWorkflow
{
    Task<AssignmentResponse?> ReceiveAsync(Guid participant, CancellationToken cancellationToken);
    Task<AssignmentResponse?> ReceiveAsync(Guid participant, string kind, CancellationToken cancellationToken);
    Task<AssignmentResponse?> ReceiveAsync(Guid participant, string kind, IReadOnlyCollection<Guid> excludedCaseIds, CancellationToken cancellationToken);
    Task<ReviewReceipt> SubmitAsync(Guid participant, Guid assignment, SubmitReviewRequest request, CancellationToken cancellationToken);
    Task<CaseStatisticsResponse> StatisticsAsync(Guid participant, Guid caseId, CancellationToken cancellationToken);
}
public interface IPermissionChecker
{
    Task<bool> HasAsync(Guid subject, string permission, AccessScope scope, Guid? resource, CancellationToken cancellationToken);
}
public sealed class AccessDeniedException : Exception;
public sealed class ConflictException(string message) : Exception(message);
public sealed class MissingResourceException : Exception;
