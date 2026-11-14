namespace BashkirSchool.Domain;

public sealed class DailyLesson
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ParticipantId { get; set; }
    public DateOnly Date { get; set; }
}
public sealed class Exercise
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LessonId { get; set; }
    public Guid CaseId { get; set; }
    public Guid ResolutionId { get; set; }
    public string Original { get; set; } = "";
    public string AcceptedTextsJson { get; set; } = "[]";
}
public sealed class ExerciseAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExerciseId { get; set; }
    public string Answer { get; set; } = "";
    public bool Recognized { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
}
