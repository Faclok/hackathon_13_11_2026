using System.Text.Json;

namespace BashkirSchool.Domain;

public enum ReviewAction { Confirm, Correct, Skip }
public enum JudgmentKind { Support, Reject }

public sealed class ReviewAssignment
{
    private ReviewAssignment() { }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ParticipantId { get; private set; }
    public Guid CaseId { get; private set; }
    public Guid ShownVariantId { get; private set; }
    public string Kind { get; private set; } = "translation-confirm";
    public string ShownVariantsJson { get; private set; } = "[]";
    public Guid[] ShownVariants => ShownVariantsJson == "[]" ? [ShownVariantId] : JsonSerializer.Deserialize<Guid[]>(ShownVariantsJson)!;
    public Guid? SchoolId { get; private set; }
    public Guid? ClassId { get; private set; }
    public Guid PolicyId { get; private set; }
    public Guid BatchId { get; private set; }
    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public bool Completed { get; private set; }

    public static ReviewAssignment Issue(Guid participant, TranslationCase translationCase, Guid? school, Guid? studyClass,
        Guid policy, DateTimeOffset now, TimeSpan lifetime, Guid? shownVariantId = null)
    {
        if (translationCase.ExpertClosed) throw new DomainException("Case is closed by an expert.");
        if (lifetime <= TimeSpan.Zero) throw new DomainException("Assignment lifetime must be positive.");
        if (school.HasValue != studyClass.HasValue || school == Guid.Empty || studyClass == Guid.Empty)
            throw new DomainException("Use a real school and class together, or null for both.");
        return new() { ParticipantId = participant, CaseId = translationCase.Id, ShownVariantId = shownVariantId ?? translationCase.ShownVariantId,
            SchoolId = school, ClassId = studyClass, PolicyId = policy, BatchId = Guid.NewGuid(), IssuedAt = now, ExpiresAt = now + lifetime };
    }

    public void Complete(Guid participant, DateTimeOffset now)
    {
        if (ParticipantId != participant) throw new DomainException("Assignment belongs to another participant.");
        if (Completed) throw new DomainException("Assignment has already been completed.");
        if (now >= ExpiresAt) throw new DomainException("Assignment has expired.");
        Completed = true;
    }

    public void SetChoices(IEnumerable<Guid> variants)
    {
        var ids = variants.ToArray();
        if (Completed || ids.Length is < 1 or > 4 || ids.Distinct().Count() != ids.Length || ids[0] != ShownVariantId)
            throw new DomainException("A choice assignment must contain one to four distinct shown variants.");
        Kind = "translation-choice";
        ShownVariantsJson = JsonSerializer.Serialize(ids);
    }

    public void SetCreation()
    {
        if (Completed) throw new DomainException("Assignment has already been completed.");
        Kind = "translation-create";
    }

    public void SetBoss()
    {
        if (Kind != "translation-choice" || Completed) throw new DomainException("Boss needs translation choices.");
        Kind = "boss";
    }

    public void SetTeacherBoss()
    {
        if (Completed || Kind != "translation-confirm") throw new DomainException("Teacher boss requires a fresh assignment.");
        Kind = "boss-teacher";
    }
}

public sealed class ReviewResponse
{
    private ReviewResponse() { }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid AssignmentId { get; private set; }
    public Guid ParticipantId { get; private set; }
    public Guid CaseId { get; private set; }
    public ReviewAction Action { get; private set; }
    public string? Correction { get; private set; }
    public Guid? SelectedVariantId { get; private set; }
    public decimal TrustAtSubmission { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }
    public bool Archived { get; private set; }

    public static ReviewResponse Submit(ReviewAssignment assignment, Guid participant, ReviewAction action,
        string? correction, decimal trust, DateTimeOffset now, bool expertClosed)
    {
        if (!Enum.IsDefined(action)) throw new DomainException("Unknown review action.");
        if (assignment.Kind == "translation-create" && action == ReviewAction.Confirm)
            throw new DomainException("An untranslated fragment needs a translation, not confirmation.");
        if (action == ReviewAction.Correct) Guard.Text(correction, nameof(correction));
        else if (correction is not null) throw new DomainException("Correction is only valid for Correct.");
        if (trust is < 0 or > 1) throw new DomainException("Invalid trust snapshot.");
        assignment.Complete(participant, now);
        return new() { AssignmentId = assignment.Id, ParticipantId = participant, CaseId = assignment.CaseId,
            Action = action, Correction = correction, TrustAtSubmission = trust, SubmittedAt = now, Archived = expertClosed };
    }

    public IReadOnlyList<VariantJudgment> Judgments(Guid shownVariantId, Guid? correctionVariantId) => Action switch
    {
        ReviewAction.Confirm => [VariantJudgment.Create(this, SelectedVariantId ?? shownVariantId, JudgmentKind.Support)],
        ReviewAction.Correct when correctionVariantId.HasValue && correctionVariantId != shownVariantId =>
            [VariantJudgment.Create(this, correctionVariantId.Value, JudgmentKind.Support)],
        ReviewAction.Skip => [],
        _ => throw new DomainException("Correction must differ from the shown text.")
    };

    public void Select(ReviewAssignment assignment, Guid variantId)
    {
        if (Action != ReviewAction.Confirm || !assignment.ShownVariants.Contains(variantId))
            throw new DomainException("Select one of the issued variants.");
        SelectedVariantId = variantId;
    }
}

public sealed class VariantJudgment
{
    private VariantJudgment() { }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ResponseId { get; private set; }
    public Guid VariantId { get; private set; }
    public JudgmentKind Kind { get; private set; }
    public static VariantJudgment Create(ReviewResponse response, Guid variant, JudgmentKind kind) =>
        new() { ResponseId = response.Id, VariantId = variant, Kind = kind };
}

public sealed record Opinion(Guid ParticipantId, Guid VariantId, JudgmentKind Kind, DateTimeOffset At, Guid ResponseId);
public sealed record VariantStatistics(Guid VariantId, int Supports, decimal? SupportRatio);
public sealed record ReviewStatistics(int UniqueParticipants, IReadOnlyList<VariantStatistics> Variants);

public static class ReviewStatisticsCalculator
{
    public static ReviewStatistics Calculate(IEnumerable<Opinion> opinions, IEnumerable<Guid> variants)
    {
        // Legacy rejection rows remain historical data, never a vote or a trust contribution.
        var all = opinions.Where(x => x.Kind == JudgmentKind.Support).ToArray();
        var latest = all.GroupBy(x => x.ParticipantId)
            .Select(g => g.OrderByDescending(x => x.At).ThenByDescending(x => x.ResponseId).First()).ToArray();
        return new(all.Select(x => x.ParticipantId).Distinct().Count(), variants.Select(id =>
        {
            var supports = latest.Count(x => x.VariantId == id && x.Kind == JudgmentKind.Support);
            return new VariantStatistics(id, supports, latest.Length == 0 ? null : (decimal)supports / latest.Length);
        }).ToArray());
    }
}
