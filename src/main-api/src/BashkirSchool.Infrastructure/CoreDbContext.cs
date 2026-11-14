using BashkirSchool.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;

namespace BashkirSchool.Infrastructure;

public sealed class Account : IdentityUser<Guid>
{
    public bool Active { get; set; } = true;
    public long SessionVersion { get; set; }
    public bool SelfRegistered { get; set; }
    public int ExpertRequiredParticipants { get; set; } = 300;
    public decimal ExpertSupportThreshold { get; set; } = .8m;
}

public sealed class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CaseId { get; set; }
    public Guid ResponseId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}

public sealed class CoreDbContext(DbContextOptions<CoreDbContext> options) : IdentityUserContext<Account, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<Fragment> Fragments => Set<Fragment>();
    public DbSet<ContentDelivery> Deliveries => Set<ContentDelivery>();
    public DbSet<TranslationCase> Cases => Set<TranslationCase>();
    public DbSet<TranslationVariant> Variants => Set<TranslationVariant>();
    public DbSet<ReviewAssignment> Assignments => Set<ReviewAssignment>();
    public DbSet<ReviewResponse> Responses => Set<ReviewResponse>();
    public DbSet<VariantJudgment> Judgments => Set<VariantJudgment>();
    public DbSet<DistributionPolicy> Policies => Set<DistributionPolicy>();
    public DbSet<School> Schools => Set<School>();
    public DbSet<StudyClass> Classes => Set<StudyClass>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<ClassInvitation> ClassInvitations => Set<ClassInvitation>();
    public DbSet<Participant> Participants => Set<Participant>();
    public DbSet<AccessAssignment> Access => Set<AccessAssignment>();
    public DbSet<ProfilePermission> Permissions => Set<ProfilePermission>();
    public DbSet<AuditEntry> Audit => Set<AuditEntry>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<ResolutionRevision> Resolutions => Set<ResolutionRevision>();
    public DbSet<TrustAssessment> TrustAssessments => Set<TrustAssessment>();
    public DbSet<TrustContribution> TrustContributions => Set<TrustContribution>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<AccountSession>().HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DailyLesson>().HasIndex(x => new { x.ParticipantId, x.Date }).IsUnique();
        b.Entity<DailyLesson>().HasOne<Participant>().WithMany().HasForeignKey(x => x.ParticipantId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Exercise>().HasOne<DailyLesson>().WithMany().HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Exercise>().HasOne<ResolutionRevision>().WithMany().HasForeignKey(x => x.ResolutionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Exercise>().Property(x => x.Kind).HasMaxLength(100).HasDefaultValue("translation-text");
        b.Entity<Exercise>().Property(x => x.LanguagePair).HasMaxLength(7).HasDefaultValue("ba-ru");
        b.Entity<Exercise>().Property(x => x.PromptJson).HasDefaultValue("{}");
        b.Entity<Exercise>().Property(x => x.AnswerKeyJson).HasDefaultValue("{}");
        b.Entity<Exercise>().Property(x => x.SchemaVersion).HasDefaultValue(1);
        b.Entity<Exercise>().HasIndex(x => new { x.LessonId, x.Kind, x.CaseId });
        b.Entity<VocabularyEntry>().Property(x => x.LanguagePair).HasMaxLength(7);
        b.Entity<VocabularyEntry>().Property(x => x.Word).HasMaxLength(200);
        b.Entity<VocabularyEntry>().Property(x => x.Translation).HasMaxLength(200);
        b.Entity<VocabularyEntry>().HasIndex(x => new { x.LanguagePair, x.Word, x.Translation }).IsUnique();
        b.Entity<SentenceAudio>().HasOne<Fragment>().WithMany().HasForeignKey(x => x.FragmentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SentenceAudio>().Property(x => x.ObjectKey).HasMaxLength(200);
        b.Entity<SentenceAudio>().HasIndex(x => new { x.FragmentId, x.ObjectKey }).IsUnique();
        b.Entity<SentenceAudio>().HasIndex(x => x.ObjectKey);
        b.Entity<ExerciseAttempt>().HasIndex(x => x.ExerciseId).IsUnique();
        b.Entity<ExerciseAttempt>().HasOne<Exercise>().WithOne().HasForeignKey<ExerciseAttempt>(x => x.ExerciseId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RefreshCredential>().HasKey(x => x.Hash);
        b.Entity<RefreshCredential>().Property(x => x.Hash).HasMaxLength(64);
        b.Entity<RefreshCredential>().HasOne<AccountSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Fragment>().HasIndex(x => new { x.ProducerId, x.ExternalId, x.LanguagePair }).IsUnique();
        b.Entity<Fragment>().Property(x => x.ExternalId).HasMaxLength(200);
        b.Entity<Fragment>().Property(x => x.LanguagePair).HasMaxLength(7);
        b.Entity<ContentDelivery>().HasIndex(x => new { x.ProducerId, x.Key }).IsUnique();
        b.Entity<ContentDelivery>().Property(x => x.Key).HasMaxLength(200);
        b.Entity<ContentDelivery>().HasOne<TranslationCase>().WithMany().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TranslationCase>().HasIndex(x => x.FragmentId).IsUnique();
        b.Entity<TranslationCase>().Property(x => x.Revision).IsConcurrencyToken();
        b.Entity<ResolutionRevision>().Ignore(x => x.Accepted).Ignore(x => x.Rejected);
        b.Entity<ResolutionRevision>().HasIndex(x => new { x.CaseId, x.Revision }).IsUnique();
        b.Entity<ResolutionRevision>().HasOne<TranslationCase>().WithMany().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ResolutionRevision>().HasOne<DistributionPolicy>().WithMany().HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TrustAssessment>().HasIndex(x => new { x.JudgmentId, x.ResolutionId }).IsUnique();
        b.Entity<TrustAssessment>().HasOne<VariantJudgment>().WithMany().HasForeignKey(x => x.JudgmentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TrustAssessment>().HasOne<ResolutionRevision>().WithMany().HasForeignKey(x => x.ResolutionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TrustContribution>().HasKey(x => x.JudgmentId);
        b.Entity<TrustContribution>().HasOne<VariantJudgment>().WithOne().HasForeignKey<TrustContribution>(x => x.JudgmentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TrustContribution>().HasOne<Participant>().WithMany().HasForeignKey(x => x.ParticipantId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TranslationCase>().HasOne<Fragment>().WithMany().HasForeignKey(x => x.FragmentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TranslationVariant>().HasOne<TranslationCase>().WithMany().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TranslationVariant>().HasAlternateKey(x => new { x.CaseId, x.Id });
        // Each assignment refers to a variant of its own case, never an arbitrary variant.
        b.Entity<ReviewAssignment>().HasOne<TranslationVariant>().WithMany()
            .HasForeignKey(x => new { x.CaseId, x.ShownVariantId }).HasPrincipalKey(x => new { x.CaseId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ReviewAssignment>().HasOne<Participant>().WithMany().HasForeignKey(x => x.ParticipantId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ReviewAssignment>().Ignore(x => x.ShownVariants);
        b.Entity<ReviewAssignment>().Property(x => x.Kind).HasMaxLength(100).HasDefaultValue("translation-confirm");
        b.Entity<ReviewAssignment>().Property(x => x.ShownVariantsJson).HasDefaultValue("[]");
        b.Entity<ReviewAssignment>().HasOne<StudyClass>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ReviewAssignment>().HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ReviewAssignment>().HasOne<DistributionPolicy>().WithMany().HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ReviewAssignment>().HasIndex(x => new { x.ParticipantId, x.CaseId, x.IssuedAt });
        b.Entity<ReviewResponse>().HasIndex(x => x.AssignmentId).IsUnique();
        b.Entity<ReviewResponse>().HasOne<ReviewAssignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<VariantJudgment>().HasIndex(x => new { x.ResponseId, x.VariantId }).IsUnique();
        b.Entity<VariantJudgment>().HasOne<ReviewResponse>().WithMany().HasForeignKey(x => x.ResponseId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<VariantJudgment>().HasOne<TranslationVariant>().WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Participant>().Ignore(x => x.Trust);
        b.Entity<Participant>().HasOne<Account>().WithOne().HasForeignKey<Participant>(x => x.Id).OnDelete(DeleteBehavior.Restrict);
        b.Entity<StudyClass>().HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<StudyClass>().HasOne<Account>().WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Enrollment>().HasIndex(x => x.ParticipantId).IsUnique();
        b.Entity<ClassInvitation>().HasIndex(x => new { x.ClassId, x.ParticipantId }).IsUnique().HasFilter("\"Status\" = 0");
        b.Entity<ClassInvitation>().HasIndex(x => new { x.ParticipantId, x.CreatedAt });
        b.Entity<ClassInvitation>().Property(x => x.Status).IsConcurrencyToken();
        b.Entity<ClassInvitation>().HasOne<StudyClass>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ClassInvitation>().HasOne<Account>().WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ClassInvitation>().HasOne<Participant>().WithMany().HasForeignKey(x => x.ParticipantId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Enrollment>().HasOne<StudyClass>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Enrollment>().HasOne<Participant>().WithMany().HasForeignKey(x => x.ParticipantId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AccessAssignment>().HasOne<Account>().WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AccessAssignment>().HasOne<AccessProfile>().WithMany().HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ProfilePermission>().HasKey(x => new { x.ProfileId, x.Permission });
        b.Entity<ProfilePermission>().HasOne<AccessProfile>().WithMany().HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DistributionPolicy>().HasIndex(x => x.Version).IsUnique();
        b.Entity<OutboxMessage>().HasIndex(x => x.ResponseId).IsUnique();
        b.Entity<OutboxMessage>().HasIndex(x => x.ProcessedAt);
        foreach (var entity in b.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties().Where(p => p.ClrType == typeof(decimal)))
            {
                property.SetPrecision(18);
                property.SetScale(12);
            }
        PermissionCatalog.Seed(b);
    }
}

public static class PermissionCatalog
{
    public static readonly IReadOnlyDictionary<string, string[]> Profiles = new Dictionary<string, string[]>
    {
        ["account"] = ["profile.read", "sessions.manage", "credentials.change"],
        ["participant"] = ["profile.read", "reviews.receive", "reviews.submit", "lessons.receive", "exercises.submit"],
        ["teacher"] = ["classes.create", "classes.read", "students.read", "students.create", "students.credentials.issue", "students.credentials.reset"],
        ["administrator"] = ["schools.manage", "classes.manage", "enrollments.manage", "teachers.manage", "experts.manage", "access.assign", "accounts.status.manage", "accounts.credentials.reset", "policies.distribution.manage", "policies.consensus.manage", "content.intake.read"],
        ["expert"] = ["expert.queue.read", "expert.case.read", "expert.translation.resolve"],
        ["producer"] = ["content.deliver", "content.intake.read"],
        ["bot"] = ["telegram.identity.attest", "telegram.links.complete"],
        ["exporter"] = ["translations.export"]
    };

    public static void Seed(ModelBuilder b)
    {
        b.Entity<AccessProfile>().HasData(Profiles.Keys.Select(id => new AccessProfile { Id = id }));
        b.Entity<ProfilePermission>().HasData(Profiles.SelectMany(p => p.Value.Select(permission =>
            new ProfilePermission { ProfileId = p.Key, Permission = permission })));
    }
}
