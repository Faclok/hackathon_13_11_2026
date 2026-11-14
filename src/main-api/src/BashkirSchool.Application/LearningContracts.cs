using System.Text.Json;

namespace BashkirSchool.Application;

/// <summary>An exercise prompt; accepted answers remain private to the server.</summary>
public sealed record ExerciseResponse(Guid Id, string Original, bool Completed)
{
    public string Kind { get; init; } = "translation-text";
    public int SchemaVersion { get; init; } = 1;
    public JsonElement Prompt { get; init; }
}
/// <summary>A stable daily lesson and its progress.</summary>
public sealed record LessonResponse(Guid Id, DateOnly Date, IReadOnlyList<ExerciseResponse> Exercises);
/// <summary>A literal translation attempt evaluated against the issued material revision.</summary>
public sealed record SubmitAttemptRequest(string Answer);
/// <summary>Recognition by the issued answer key; unrecognized text is not a verdict about linguistic correctness.</summary>
public sealed record AttemptResponse(Guid Id, bool Recognized);

/// <summary>Requests a supported exercise; no answer key or author is accepted.</summary>
public sealed record IssueExerciseRequest(string Kind, Guid? CaseId = null, string LanguagePair = "ba-ru");
/// <summary>Kind-specific answer object, validated by the issued handler and schema version.</summary>
public sealed record SubmitExerciseAnswerRequest(JsonElement Answer);
public sealed record ExerciseKindResponse(string Kind, int SchemaVersion);
public sealed record ExerciseItem(string Id, string Text);
public sealed record WordPairAnswer(string LeftId, string RightId);
public sealed record TextExerciseAnswer(string Text);
public sealed record GapExerciseAnswer(string? Text = null, string? OptionId = null);
public sealed record WordMatchAnswer(IReadOnlyList<WordPairAnswer> Pairs);
public sealed record SentenceOrderAnswer(IReadOnlyList<string> TokenIds);
public sealed record TextExercisePrompt(string Text, string SourceLanguage, string TargetLanguage);
public sealed record AudioExercisePrompt(Guid AudioId, string? AudioUrl, string SourceLanguage, string TargetLanguage);
public sealed record GapExercisePrompt(string Before, string After, IReadOnlyList<ExerciseItem> Options, bool AllowCustom);
public sealed record SentenceOrderPrompt(IReadOnlyList<ExerciseItem> Tokens);
public sealed record WordMatchPrompt(IReadOnlyList<ExerciseItem> Left, IReadOnlyList<ExerciseItem> Right, string LanguagePair);
public sealed record VocabularyItemRequest(string Word, string Translation);
public sealed record DeliverVocabularyRequest(string LanguagePair, IReadOnlyList<VocabularyItemRequest> Entries);
public sealed record VocabularyDeliveryResponse(IReadOnlyList<Guid> Ids);
public sealed record AudioAssetResponse(Guid Id, Guid FragmentId);

public interface ISpeechSynthesizer
{
    Task<SynthesizedSpeech> SynthesizeAsync(string text, string language, CancellationToken ct);
}
public sealed record SynthesizedSpeech(byte[] Data, string ContentType, string Extension);
public interface IAudioStorage
{
    Task<bool> ExistsAsync(string key, CancellationToken ct);
    Task UploadAsync(string key, SynthesizedSpeech speech, CancellationToken ct);
    Task<string> ListenUrlAsync(string key, CancellationToken ct);
}
