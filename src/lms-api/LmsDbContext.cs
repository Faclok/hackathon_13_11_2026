using Microsoft.EntityFrameworkCore;

public sealed class LmsDbContext(DbContextOptions<LmsDbContext> options) : DbContext(options)
{
    public DbSet<LmsCourse> Courses => Set<LmsCourse>();
    public DbSet<LmsLesson> Lessons => Set<LmsLesson>();
    public DbSet<LmsProgress> Progress => Set<LmsProgress>();

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
