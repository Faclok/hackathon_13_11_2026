using Microsoft.EntityFrameworkCore;

public sealed class BotDbContext(DbContextOptions<BotDbContext> options) : DbContext(options)
{
    public DbSet<BotState> States => Set<BotState>();
    public DbSet<BotCursor> Cursors => Set<BotCursor>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<BotState>(e =>
        {
            e.ToTable("bot_states"); e.HasKey(x => x.TelegramId);
            e.Property(x => x.TelegramId).HasColumnName("telegram_id");
            e.Property(x => x.State).HasColumnName("state");
            e.Property(x => x.TaskId).HasColumnName("task_id");
            e.Property(x => x.AnswerMessageId).HasColumnName("answer_message_id");
            e.Property(x => x.AnswerExerciseId).HasColumnName("answer_exercise_id");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });
        model.Entity<BotCursor>(e =>
        {
            e.ToTable("bot_cursors"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.NextOffset).HasColumnName("next_offset");
        });
    }
}

public sealed class BotState
{
    public long TelegramId { get; set; }
    public string State { get; set; } = "idle";
    public Guid? TaskId { get; set; }
    public int? AnswerMessageId { get; set; }
    public Guid? AnswerExerciseId { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
public sealed class BotCursor
{
    public int Id { get; set; } = 1;
    public long NextOffset { get; set; }
}
