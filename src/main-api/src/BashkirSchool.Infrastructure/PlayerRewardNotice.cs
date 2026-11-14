namespace BashkirSchool.Infrastructure;

// Persist delayed rewards until the participant explicitly dismisses them.
public sealed class PlayerRewardNotice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ParticipantId { get; set; }
    public Guid JudgmentId { get; set; }
    public Guid ResolutionId { get; set; }
    public int Coins { get; set; }
    public int Xp { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool Seen { get; set; }
}

public sealed record PlayerRewardNoticeInfo(Guid Id, int Coins, int Xp, DateTimeOffset CreatedAt);
