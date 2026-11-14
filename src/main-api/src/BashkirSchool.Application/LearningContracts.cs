namespace BashkirSchool.Application;

/// <summary>An exercise prompt; accepted answers remain private to the server.</summary>
public sealed record ExerciseResponse(Guid Id, string Original, bool Completed);
/// <summary>A stable daily lesson and its progress.</summary>
public sealed record LessonResponse(Guid Id, DateOnly Date, IReadOnlyList<ExerciseResponse> Exercises);
/// <summary>A literal translation attempt evaluated against the issued material revision.</summary>
public sealed record SubmitAttemptRequest(string Answer);
/// <summary>Recognition by the issued answer key; unrecognized text is not a verdict about linguistic correctness.</summary>
public sealed record AttemptResponse(Guid Id, bool Recognized);
