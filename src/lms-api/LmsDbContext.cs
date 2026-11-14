using Microsoft.EntityFrameworkCore;

public sealed class LmsDbContext(DbContextOptions<LmsDbContext> options) : DbContext(options)
{
    public DbSet<LmsCourse> Courses => Set<LmsCourse>();
    public DbSet<LmsLesson> Lessons => Set<LmsLesson>();
    public DbSet<LmsProgress> Progress => Set<LmsProgress>();
    public DbSet<TeacherClass> TeacherClasses => Set<TeacherClass>();
    public DbSet<ClassMember> ClassMembers => Set<ClassMember>();
    public DbSet<TeacherTranslation> TeacherTranslations => Set<TeacherTranslation>();
    public DbSet<ClassTask> ClassTasks => Set<ClassTask>();
    public DbSet<TaskSubmission> TaskSubmissions => Set<TaskSubmission>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<LmsCourse>(e =>
        {
            e.ToTable("lms_courses"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.Title).HasColumnName("title");
            e.Property(x => x.Description).HasColumnName("description");
        });
        model.Entity<LmsLesson>(e =>
        {
            e.ToTable("lms_lessons"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.CourseId).HasColumnName("course_id");
            e.Property(x => x.Position).HasColumnName("position");
            e.Property(x => x.Title).HasColumnName("title");
            e.Property(x => x.Body).HasColumnName("body");
            e.HasIndex(x => new { x.CourseId, x.Position }).IsUnique();
            e.HasOne<LmsCourse>().WithMany().HasForeignKey(x => x.CourseId);
        });
        model.Entity<LmsProgress>(e =>
        {
            e.ToTable("lms_progress"); e.HasKey(x => new { x.UserId, x.LessonId });
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.LessonId).HasColumnName("lesson_id");
            e.Property(x => x.CompletedAt).HasColumnName("completed_at").HasDefaultValueSql("now()");
            e.HasOne<LmsLesson>().WithMany().HasForeignKey(x => x.LessonId);
        });
        model.Entity<TeacherClass>(e =>
        {
            e.ToTable("lms_teacher_classes"); e.HasKey(x => x.Id);
            e.HasIndex(x => x.TeacherId);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(2000);
        });
        model.Entity<ClassMember>(e =>
        {
            e.ToTable("lms_class_members"); e.HasKey(x => new { x.ClassId, x.UserId });
            e.HasIndex(x => x.UserId);
            e.Property(x => x.Username).HasMaxLength(100);
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.HasOne<TeacherClass>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<TeacherTranslation>(e =>
        {
            e.ToTable("lms_teacher_translations"); e.HasKey(x => x.Id);
            e.HasIndex(x => x.TeacherId);
            e.Property(x => x.LanguagePair).HasMaxLength(7);
            e.Property(x => x.SourceText).HasMaxLength(5000);
            e.Property(x => x.TranslationText).HasMaxLength(5000);
        });
        model.Entity<ClassTask>(e =>
        {
            e.ToTable("lms_class_tasks"); e.HasKey(x => x.Id);
            e.HasIndex(x => x.ClassId);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.SourceText).HasMaxLength(5000);
            e.Property(x => x.ReferenceTranslation).HasMaxLength(5000);
            e.Property(x => x.TranslationStatus).HasMaxLength(40);
            e.HasOne<TeacherClass>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<TaskSubmission>(e =>
        {
            e.ToTable("lms_task_submissions"); e.HasKey(x => new { x.TaskId, x.UserId });
            e.HasOne<ClassTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Answer).HasMaxLength(5000);
            e.Property(x => x.TeacherComment).HasMaxLength(1000);
        });
    }
}

public sealed class LmsCourse
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
}
public sealed class LmsLesson
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public int Position { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
}
public sealed class LmsProgress
{
    public Guid UserId { get; set; }
    public int LessonId { get; set; }
    public DateTime CompletedAt { get; set; }
}

public sealed class TeacherClass
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TeacherId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class ClassMember
{
    public Guid ClassId { get; set; }
    public Guid UserId { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class TeacherTranslation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TeacherId { get; set; }
    public string SourceText { get; set; } = "";
    public string TranslationText { get; set; } = "";
    public string LanguagePair { get; set; } = "ba-ru";
    public Guid? CoreCaseId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class ClassTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClassId { get; set; }
    public string Title { get; set; } = "";
    public string SourceText { get; set; } = "";
    public string ReferenceTranslation { get; set; } = "";
    public string TranslationStatus { get; set; } = "";
    public int TranslationRating { get; set; }
    public Guid? CatalogCaseId { get; set; }
    public Guid? TeacherTranslationId { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class TaskSubmission
{
    public Guid TaskId { get; set; }
    public Guid UserId { get; set; }
    public string Answer { get; set; } = "";
    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool? TeacherCorrect { get; set; }
    public string TeacherComment { get; set; } = "";
    public DateTimeOffset? ReviewedAt { get; set; }
}
