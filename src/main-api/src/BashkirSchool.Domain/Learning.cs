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
    public Guid? CaseId { get; set; }
    public Guid? ResolutionId { get; set; }
    public string Kind { get; set; } = "translation-text";
    public string LanguagePair { get; set; } = "ba-ru";
    public int SchemaVersion { get; set; } = 1;
    public string PromptJson { get; set; } = "{}";
    public string AnswerKeyJson { get; set; } = "{}";
    public string Original { get; set; } = "";
    public string AcceptedTextsJson { get; set; } = "[]";
}

public sealed class VocabularyEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string LanguagePair { get; set; } = "";
    public string Word { get; set; } = "";
    public string Translation { get; set; } = "";
}

// An asset becomes visible to exercises only after synthesis and upload succeed.
public sealed class SentenceAudio
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FragmentId { get; set; }
    public string ObjectKey { get; set; } = "";
    public string ContentType { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
public sealed class ExerciseAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExerciseId { get; set; }
    public string Answer { get; set; } = "";
    public bool Recognized { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
}
