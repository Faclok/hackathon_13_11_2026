using System.Text.Json;

namespace BashkirSchool.Domain;

public enum ResolutionKind { Pending, Community, Expert }
public enum AssessmentOutcome { Pending, Correct, Incorrect }

public sealed class ResolutionRevision
{
    private ResolutionRevision() { }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CaseId { get; private set; }
    public long Revision { get; private set; }
    public ResolutionKind Kind { get; private set; }
    public Guid? PrimaryVariantId { get; private set; }
    public string AcceptedJson { get; private set; } = "[]";
    public string RejectedJson { get; private set; } = "[]";
    public Guid PolicyId { get; private set; }
    public Guid? DecidedBy { get; private set; }
    public string EvidenceJson { get; private set; } = "{}";
    public string? Comment { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid[] Accepted => JsonSerializer.Deserialize<Guid[]>(AcceptedJson)!;
    public Guid[] Rejected => JsonSerializer.Deserialize<Guid[]>(RejectedJson)!;

    public static ResolutionRevision Create(Guid caseId, long revision, ResolutionKind kind, Guid? primary,
        IEnumerable<Guid> accepted, IEnumerable<Guid> rejected, Guid policyId, Guid? actor, string evidence,
        string? comment, DateTimeOffset now)
    {
        var valid = accepted.Distinct().Order().ToArray();
        var invalid = rejected.Distinct().Order().ToArray();
        if (revision < 1 || !Enum.IsDefined(kind) || valid.Intersect(invalid).Any() || valid.Length > 5 ||
            (primary.HasValue != valid.Contains(primary ?? Guid.Empty)) ||
            (kind == ResolutionKind.Pending && (primary.HasValue || valid.Length != 0 || invalid.Length != 0)) ||
            (kind != ResolutionKind.Pending && !primary.HasValue) || (kind == ResolutionKind.Expert && !actor.HasValue))
            throw new DomainException("Invalid translation resolution.");
        return new() { CaseId = caseId, Revision = revision, Kind = kind, PrimaryVariantId = primary,
            AcceptedJson = JsonSerializer.Serialize(valid), RejectedJson = JsonSerializer.Serialize(invalid),
            PolicyId = policyId, DecidedBy = actor, EvidenceJson = evidence, Comment = comment, CreatedAt = now };
    }

    // A supplied dataset may attest expert verification without naming the individual expert.
    public static ResolutionRevision ImportVerified(Guid caseId, Guid variantId, Guid policyId, string evidence, DateTimeOffset now) =>
        new() { CaseId = caseId, Revision = 1, Kind = ResolutionKind.Expert, PrimaryVariantId = variantId,
            AcceptedJson = JsonSerializer.Serialize(new[] { variantId }), PolicyId = policyId,
            DecidedBy = null, EvidenceJson = evidence, Comment = "Проверено экспертом по данным источника", CreatedAt = now };
}

public sealed class TrustAssessment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JudgmentId { get; set; }
    public Guid ResolutionId { get; set; }
    public AssessmentOutcome Outcome { get; set; }
    public DateTimeOffset AssessedAt { get; set; }
}

public sealed class TrustContribution
{
    public Guid JudgmentId { get; set; }
    public Guid ParticipantId { get; set; }
    public Guid? ResolutionId { get; set; }
    public long Revision { get; set; }
    public int Correct { get; set; }
    public int Total { get; set; }
}
