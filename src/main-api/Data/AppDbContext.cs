using Microsoft.EntityFrameworkCore;

/// <summary>EF Core model for accounts, source fragments, answers, translations, and points.</summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<UserSession> Sessions => Set<UserSession>();
    public DbSet<SourceItem> Sources => Set<SourceItem>();
    public DbSet<Fragment> Fragments => Set<Fragment>();
    public DbSet<TranslationCandidate> Candidates => Set<TranslationCandidate>();
    public DbSet<DailyTask> DailyTasks => Set<DailyTask>();
    public DbSet<UserSubmission> Submissions => Set<UserSubmission>();
    public DbSet<TranslationRecord> Translations => Set<TranslationRecord>();
    public DbSet<PointEvent> PointEvents => Set<PointEvent>();

    /// <summary>Defines table names, keys, defaults, indexes, and foreign keys.</summary>
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
        model.Entity<TranslationRecord>(e =>
        {
            e.ToTable("translations"); e.HasKey(x => new { x.FragmentId, x.LangPair });
            e.Property(x => x.Source).HasColumnType("jsonb");
            e.Property(x => x.Confirmed).HasDefaultValue(0);
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
        // Match the database's snake_case naming without repeating column mappings.
        foreach (var entity in model.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                property.SetColumnName(SnakeCase(property.Name));
    }

    private static string SnakeCase(string value) => string.Concat(value.Select((ch, i) =>
        i > 0 && char.IsUpper(ch) ? "_" + char.ToLowerInvariant(ch) : char.ToLowerInvariant(ch).ToString()));
}

/// <summary>Web or Telegram participant; either username or Telegram ID can identify one.</summary>
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
/// <summary>Hashed bearer token with its owner and expiry time.</summary>
public sealed class UserSession
{
    public string TokenHash { get; set; } = "";
    public Guid UserId { get; set; }
    public DateTime ExpiresAt { get; set; }
}
/// <summary>Metadata for a document or dataset that supplies fragments.</summary>
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
/// <summary>Bashkir text unit linked to a source item.</summary>
public sealed class Fragment
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public string TextBa { get; set; } = "";
    public string QualityStatus { get; set; } = "demo";
}
/// <summary>Imported or demonstration translation shown as a candidate, never a vote.</summary>
public sealed class TranslationCandidate
{
    public Guid Id { get; set; }
    public Guid FragmentId { get; set; }
    public string LangPair { get; set; } = "ba-ru";
    public string Text { get; set; } = "";
    public string Origin { get; set; } = "demo";
}
/// <summary>One assigned fragment and candidate per user, language pair, and day.</summary>
public sealed class DailyTask
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid FragmentId { get; set; }
    public Guid CandidateId { get; set; }
    public string LangPair { get; set; } = "ba-ru";
    public DateOnly TaskDate { get; set; }
}
/// <summary>A user's current confirm or correct answer for one daily task.</summary>
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
/// <summary>Consensus result for a fragment and language pair, including source snapshot.</summary>
public sealed class TranslationRecord
{
    public Guid FragmentId { get; set; }
    public string TextBa { get; set; } = "";
    public string LangPair { get; set; } = "ba-ru";
    public string? Translation { get; set; }
    /// <summary>JSONB coordinates such as document, file, chapter, and time.</summary>
    public string Source { get; set; } = "{}";
    public string Status { get; set; } = "collecting";
    /// <summary>Number of distinct users supporting the leading answer.</summary>
    public int Confirmed { get; set; }
    public DateTime UpdatedAt { get; set; }
}
/// <summary>Points awarded for a submission; the unique submission ID prevents duplicates.</summary>
public sealed class PointEvent
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid SubmissionId { get; set; }
    public int Points { get; set; }
    public DateTime CreatedAt { get; set; }
}
