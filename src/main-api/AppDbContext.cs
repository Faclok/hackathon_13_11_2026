using Microsoft.EntityFrameworkCore;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<UserSession> Sessions => Set<UserSession>();
    public DbSet<SourceItem> Sources => Set<SourceItem>();
    public DbSet<Fragment> Fragments => Set<Fragment>();
    public DbSet<TranslationCandidate> Candidates => Set<TranslationCandidate>();
    public DbSet<DailyTask> DailyTasks => Set<DailyTask>();
    public DbSet<UserSubmission> Submissions => Set<UserSubmission>();
    public DbSet<ConsensusDecision> Decisions => Set<ConsensusDecision>();
    public DbSet<PointEvent> PointEvents => Set<PointEvent>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<AppUser>(e =>
        {
            e.ToTable("app_users"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.LangPair).HasDefaultValue("ba-ru");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.HasIndex(x => x.Username).IsUnique();
            e.HasIndex(x => x.TelegramId).IsUnique();
        });
        model.Entity<UserSession>(e =>
        {
            e.ToTable("sessions"); e.HasKey(x => x.TokenHash);
            e.HasIndex(x => x.UserId);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<SourceItem>(e =>
        {
            e.ToTable("source_items"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.HasIndex(x => new { x.Dataset, x.OriginalId }).IsUnique();
        });
        model.Entity<Fragment>(e =>
        {
            e.ToTable("fragments"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.QualityStatus).HasDefaultValue("demo");
            e.HasOne<SourceItem>().WithMany().HasForeignKey(x => x.SourceId);
        });
        model.Entity<TranslationCandidate>(e =>
        {
            e.ToTable("translation_candidates"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.LangPair).HasDefaultValue("ba-ru");
            e.Property(x => x.Origin).HasDefaultValue("demo");
            e.HasIndex(x => new { x.FragmentId, x.LangPair }).IsUnique();
            e.HasOne<Fragment>().WithMany().HasForeignKey(x => x.FragmentId);
        });
        model.Entity<DailyTask>(e =>
        {
            e.ToTable("daily_tasks"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.HasIndex(x => new { x.UserId, x.LangPair, x.TaskDate }).IsUnique();
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId);
            e.HasOne<Fragment>().WithMany().HasForeignKey(x => x.FragmentId);
            e.HasOne<TranslationCandidate>().WithMany().HasForeignKey(x => x.CandidateId);
        });
        model.Entity<UserSubmission>(e =>
        {
            e.ToTable("submissions"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
            e.HasIndex(x => x.TaskId).IsUnique();
            e.HasIndex(x => new { x.FragmentId, x.LangPair });
            e.HasOne<DailyTask>().WithMany().HasForeignKey(x => x.TaskId);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId);
            e.HasOne<Fragment>().WithMany().HasForeignKey(x => x.FragmentId);
        });
        model.Entity<ConsensusDecision>(e =>
        {
            e.ToTable("consensus_decisions"); e.HasKey(x => new { x.FragmentId, x.LangPair });
            e.Property(x => x.Confirmations).HasDefaultValue(0);
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
            e.HasOne<Fragment>().WithMany().HasForeignKey(x => x.FragmentId);
        });
        model.Entity<PointEvent>(e =>
        {
            e.ToTable("point_events"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.HasIndex(x => x.SubmissionId).IsUnique();
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId);
            e.HasOne<UserSubmission>().WithMany().HasForeignKey(x => x.SubmissionId);
        });
        foreach (var entity in model.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                property.SetColumnName(SnakeCase(property.Name));
    }

    private static string SnakeCase(string value) => string.Concat(value.Select((ch, i) =>
        i > 0 && char.IsUpper(ch) ? "_" + char.ToLowerInvariant(ch) : char.ToLowerInvariant(ch).ToString()));
}

public sealed class AppUser
{
    public Guid Id { get; set; }
    public string? Username { get; set; }
    public string? PasswordHash { get; set; }
    public string DisplayName { get; set; } = "";
    public long? TelegramId { get; set; }
    public string LangPair { get; set; } = "ba-ru";
    public DateTime CreatedAt { get; set; }
}
public sealed class UserSession
{
    public string TokenHash { get; set; } = "";
    public Guid UserId { get; set; }
    public DateTime ExpiresAt { get; set; }
}
public sealed class SourceItem
{
    public Guid Id { get; set; }
    public string Dataset { get; set; } = "";
    public string OriginalId { get; set; } = "";
    public string Title { get; set; } = "";
    public string? SourceUrl { get; set; }
    public string Attribution { get; set; } = "";
    public string License { get; set; } = "";
}
public sealed class Fragment
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public string TextBa { get; set; } = "";
    public string QualityStatus { get; set; } = "demo";
}
public sealed class TranslationCandidate
{
    public Guid Id { get; set; }
    public Guid FragmentId { get; set; }
    public string LangPair { get; set; } = "ba-ru";
    public string Text { get; set; } = "";
    public string Origin { get; set; } = "demo";
}
public sealed class DailyTask
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid FragmentId { get; set; }
    public Guid CandidateId { get; set; }
    public string LangPair { get; set; } = "ba-ru";
    public DateOnly TaskDate { get; set; }
}
public sealed class UserSubmission
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public Guid UserId { get; set; }
    public Guid FragmentId { get; set; }
    public string LangPair { get; set; } = "ba-ru";
    public string Action { get; set; } = "";
    public string AnswerText { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
}
public sealed class ConsensusDecision
{
    public Guid FragmentId { get; set; }
    public string LangPair { get; set; } = "ba-ru";
    public string? Translation { get; set; }
    public string Status { get; set; } = "collecting";
    public int Confirmations { get; set; }
    public DateTime UpdatedAt { get; set; }
}
public sealed class PointEvent
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid SubmissionId { get; set; }
    public int Points { get; set; }
    public DateTime CreatedAt { get; set; }
}
