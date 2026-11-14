using System.Text.Json.Serialization;
using BashkirSchool.Domain;

namespace BashkirSchool.Application;

/// <summary>Expert's final decision, protected against a stale revision.</summary>
public sealed record ResolveCaseRequest(long ExpectedRevision, Guid PrimaryVariantId, Guid[] AdditionalVariantIds,
    Guid[] RejectedVariantIds, string? Comment = null);
/// <summary>Adds an expert's own rendering before the final decision.</summary>
public sealed record ProposeTranslationRequest(string Text, long ExpectedRevision);
/// <summary>A historical decision and its accepted/rejected variants.</summary>
public sealed record ResolutionResponse(Guid Id, long Revision, ResolutionKind Kind, Guid? PrimaryVariantId,
    Guid[] AcceptedVariantIds, Guid[] RejectedVariantIds, DateTimeOffset CreatedAt);
/// <summary>One candidate's public evidence, with no participant trust values.</summary>
public sealed record VariantEvidence(Guid Id, string Text, int Supports, decimal? SupportRatio);
/// <summary>Material and evidence available to an authorized expert.</summary>
public sealed record ExpertCaseResponse(Guid CaseId, long Revision, string Original, string LanguagePair,
    SourceReference Source, bool Closed, bool Disputed, ResolutionResponse? Resolution, IReadOnlyList<VariantEvidence> Variants);
/// <summary>A queued case, ordered by disagreement among sufficiently sampled cases.</summary>
public sealed record ExpertQueueItem(Guid CaseId, long Revision, bool Disputed, int Observations, decimal? LeadingSupport, DateTimeOffset CreatedAt);
/// <summary>Export in the original intake team's column format.</summary>
public sealed record ExportRow([property: JsonPropertyName("fragment_id")] string FragmentId,
    [property: JsonPropertyName("text_ba")] string TextBa, [property: JsonPropertyName("lang_pair")] string LanguagePair,
    string Translation, SourceReference Source, int Confirmed, string Status);
