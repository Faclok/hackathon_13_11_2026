using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BashkirSchool.Domain;

namespace BashkirSchool.Application;

public sealed record ExerciseSentence(Guid CaseId, Guid? ResolutionId, string Original, string LanguagePair,
    IReadOnlyList<string> Accepted, Guid? AudioId);
public sealed record ExerciseWord(Guid Id, string Word, string Translation);
public sealed record ExerciseMaterial(ExerciseSentence? Sentence, IReadOnlyList<ExerciseWord> Words, string LanguagePair,
    IReadOnlyList<string>? SourceAlternatives = null, IReadOnlyList<string>? TranslationAlternatives = null);
public sealed record ExerciseDraft(string Original, JsonElement Prompt, JsonElement AnswerKey);

/// <summary>One kind and immutable answer schema. New versions keep old issued exercises evaluable.</summary>
public interface IExerciseHandler
{
    string Kind { get; }
    int SchemaVersion => 1;
    bool RequiresSentence => true;
    bool RequiresAudio => false;
    bool RequiresAcceptedTranslation => false;
    ExerciseDraft? Create(ExerciseMaterial material);
    bool Evaluate(JsonElement answerKey, JsonElement answer);
    string? CorrectAnswer(JsonElement answerKey) => null;
}

public sealed class ExerciseRegistry
{
    private readonly IReadOnlyDictionary<(string, int), IExerciseHandler> handlers;
    public ExerciseRegistry(IEnumerable<IExerciseHandler> handlers)
    {
        this.handlers = handlers.ToDictionary(h => (h.Kind, h.SchemaVersion));
    }
    public IReadOnlyList<ExerciseKindResponse> Kinds => handlers.Keys.OrderBy(x => x.Item1)
        .ThenBy(x => x.Item2).Select(x => new ExerciseKindResponse(x.Item1, x.Item2)).ToArray();
    public IExerciseHandler Latest(string kind) => handlers.Where(x => x.Key.Item1 == kind).OrderByDescending(x => x.Key.Item2)
        .Select(x => x.Value).FirstOrDefault() ?? throw new DomainException("Unsupported exercise kind.");
    public IExerciseHandler Get(string kind, int version = 1) => kind is not null && handlers.TryGetValue((kind, version), out var handler)
        ? handler : throw new DomainException("Unsupported exercise kind or schema version.");
}

public static class ExerciseJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false
    };
    public static JsonElement Element<T>(T value) => JsonSerializer.SerializeToElement(value, Options);
    public static T Read<T>(JsonElement value)
    {
        try { return value.Deserialize<T>(Options) ?? throw new DomainException("Answer is required."); }
        catch (JsonException) { throw new DomainException("Invalid exercise answer structure."); }
    }
    public static string Text(string? value) => Guard.Text(value, "text");
    public static T[] Shuffle<T>(IEnumerable<T> values)
    {
        var items = values.ToArray();
        Random.Shared.Shuffle(items);
        return items;
    }
}

public sealed class TranslationTextHandler : IExerciseHandler
{
    public string Kind => "translation-text";
    public bool RequiresAcceptedTranslation => true;
    public ExerciseDraft? Create(ExerciseMaterial material)
    {
        if (material.Sentence is not { Accepted.Count: > 0 } s) return null;
        var languages = s.LanguagePair.Split('-');
        return new(s.Original, ExerciseJson.Element(new TextExercisePrompt(s.Original, languages[0], languages[1])), ExerciseJson.Element(s.Accepted));
    }
    public bool Evaluate(JsonElement key, JsonElement answer) => ExerciseJson.Read<string[]>(key)
        .Contains(ExerciseJson.Text(ExerciseJson.Read<TextExerciseAnswer>(answer).Text), StringComparer.Ordinal);
}

public sealed class DictationHandler : IExerciseHandler
{
    public string Kind => "audio-dictation";
    public bool RequiresAudio => true;
    public ExerciseDraft? Create(ExerciseMaterial material)
    {
        if (material.Sentence is not { AudioId: not null } s) return null;
        var language = s.LanguagePair.Split('-')[0];
        return new("", ExerciseJson.Element(new AudioExercisePrompt(s.AudioId.Value, null, language, language)), ExerciseJson.Element(new[] { s.Original }));
    }
    public bool Evaluate(JsonElement key, JsonElement answer) => ExerciseJson.Read<string[]>(key)
        .Contains(ExerciseJson.Text(ExerciseJson.Read<TextExerciseAnswer>(answer).Text), StringComparer.Ordinal);
}

public sealed class AudioTranslationHandler : IExerciseHandler
{
    public string Kind => "audio-translation";
    public bool RequiresAudio => true;
    public bool RequiresAcceptedTranslation => true;
    public ExerciseDraft? Create(ExerciseMaterial material)
    {
        if (material.Sentence is not { AudioId: not null, Accepted.Count: > 0, ResolutionId: not null } s) return null;
        var languages = s.LanguagePair.Split('-');
        return new("", ExerciseJson.Element(new AudioExercisePrompt(s.AudioId.Value, null, languages[0], languages[1])), ExerciseJson.Element(s.Accepted));
    }
    public bool Evaluate(JsonElement key, JsonElement answer) => ExerciseJson.Read<string[]>(key)
        .Contains(ExerciseJson.Text(ExerciseJson.Read<TextExerciseAnswer>(answer).Text), StringComparer.Ordinal);
}

public static class ChoiceExercises
{
    private static readonly string[] BashkirFallback = ["Һаумыһығыҙ!", "Рәхмәт!", "Һау булығыҙ!", "Эйе.", "Юҡ."];
    private static readonly string[] RussianFallback = ["Здравствуйте!", "Спасибо!", "До свидания!", "Да.", "Нет."];
    private static readonly string[] EnglishFallback = ["Hello!", "Thank you!", "Goodbye!", "Yes.", "No."];

    public static (ExerciseItem[] Options, ChoiceAnswerKey Key)? Create(string correct,
        IEnumerable<string> accepted, IEnumerable<string>? alternatives, string language)
    {
        var excluded = accepted.Append(correct).Select(Normalize).ToHashSet(StringComparer.Ordinal);
        var fallback = language switch { "ba" => BashkirFallback, "ru" => RussianFallback, "en" => EnglishFallback, _ => [] };
        var real = ExerciseJson.Shuffle((alternatives ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x) && !excluded.Contains(Normalize(x)))
            .GroupBy(Normalize).Select(x => x.First()));
        var wrong = real.Concat(ExerciseJson.Shuffle(fallback))
            .Where(x => !excluded.Contains(Normalize(x)))
            .GroupBy(Normalize).Select(x => x.First()).Take(2).ToArray();
        if (wrong.Length != 2) return null;
        var correctItem = new ExerciseItem(Guid.NewGuid().ToString("N"), correct);
        var options = ExerciseJson.Shuffle(wrong.Select(x => new ExerciseItem(Guid.NewGuid().ToString("N"), x)).Append(correctItem));
        return (options, new ChoiceAnswerKey(correctItem.Id, correct, options));
    }

    public static bool Evaluate(JsonElement key, JsonElement answer)
    {
        var issued = ExerciseJson.Read<ChoiceAnswerKey>(key);
        var selected = ExerciseJson.Read<ChoiceExerciseAnswer>(answer).OptionId;
        if (!issued.Options.Any(x => x.Id == selected)) throw new DomainException("Option was not issued.");
        return selected == issued.CorrectId;
    }
    public static string CorrectAnswer(JsonElement key) => ExerciseJson.Read<ChoiceAnswerKey>(key).CorrectText;
    private static string Normalize(string value) => new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}

public sealed class TranslationChoiceHandler : IExerciseHandler
{
    public string Kind => "translation-text";
    public int SchemaVersion => 2;
    public bool RequiresAcceptedTranslation => true;
    public ExerciseDraft? Create(ExerciseMaterial material)
    {
        if (material.Sentence is not { Accepted.Count: > 0 } sentence) return null;
        var languages = sentence.LanguagePair.Split('-');
        var choices = ChoiceExercises.Create(sentence.Accepted[0], sentence.Accepted,
            material.TranslationAlternatives, languages[1]);
        if (choices is null) return null;
        var tokens = Regex.Matches(sentence.Original, @"\p{L}[\p{L}\p{M}]*(?:[-’'][\p{L}\p{M}]+)*")
            .Select(x => x.Value).ToArray();
        var hints = material.Words.Where(x => tokens.Contains(x.Word, StringComparer.OrdinalIgnoreCase))
            .GroupBy(x => x.Word, StringComparer.OrdinalIgnoreCase)
            .Select(x => new WordHint(x.Key, x.First().Translation)).ToArray();
        if (tokens.Length == 1 && hints.Length == 0)
            hints = [new WordHint(tokens[0], sentence.Accepted[0])];
        return new(sentence.Original, ExerciseJson.Element(new ChoiceTextPrompt(sentence.Original, languages[0], languages[1],
            choices.Value.Options, hints, sentence.Accepted[0])), ExerciseJson.Element(choices.Value.Key));
    }
    public bool Evaluate(JsonElement key, JsonElement answer) => ChoiceExercises.Evaluate(key, answer);
    public string? CorrectAnswer(JsonElement key) => ChoiceExercises.CorrectAnswer(key);
}

public sealed class DictationChoiceHandler : IExerciseHandler
{
    public string Kind => "audio-dictation";
    public int SchemaVersion => 2;
    public bool RequiresAudio => true;
    public ExerciseDraft? Create(ExerciseMaterial material)
    {
        if (material.Sentence is not { AudioId: not null } sentence) return null;
        var language = sentence.LanguagePair.Split('-')[0];
        var choices = ChoiceExercises.Create(sentence.Original, [sentence.Original], material.SourceAlternatives, language);
        if (choices is null) return null;
        return new("", ExerciseJson.Element(new ChoiceAudioPrompt(sentence.AudioId.Value, null, language, language,
            sentence.Original, choices.Value.Options)), ExerciseJson.Element(choices.Value.Key));
    }
    public bool Evaluate(JsonElement key, JsonElement answer) => ChoiceExercises.Evaluate(key, answer);
    public string? CorrectAnswer(JsonElement key) => ChoiceExercises.CorrectAnswer(key);
}

public sealed class AudioTranslationChoiceHandler : IExerciseHandler
{
    public string Kind => "audio-translation";
    public int SchemaVersion => 2;
    public bool RequiresAudio => true;
    public bool RequiresAcceptedTranslation => true;
    public ExerciseDraft? Create(ExerciseMaterial material)
    {
        if (material.Sentence is not { AudioId: not null, Accepted.Count: > 0, ResolutionId: not null } sentence) return null;
        var languages = sentence.LanguagePair.Split('-');
        var choices = ChoiceExercises.Create(sentence.Accepted[0], sentence.Accepted,
            material.TranslationAlternatives, languages[1]);
        if (choices is null) return null;
        return new("", ExerciseJson.Element(new ChoiceAudioPrompt(sentence.AudioId.Value, null, languages[0], languages[1],
            sentence.Original, choices.Value.Options)), ExerciseJson.Element(choices.Value.Key));
    }
    public bool Evaluate(JsonElement key, JsonElement answer) => ChoiceExercises.Evaluate(key, answer);
    public string? CorrectAnswer(JsonElement key) => ChoiceExercises.CorrectAnswer(key);
}

public sealed record GapAnswerKey(string Text, IReadOnlyList<ExerciseItem> Options);
public sealed class MissingWordHandler : IExerciseHandler
{
    public string Kind => "missing-word";
    // Matches Bashkir letters, including internal apostrophes/hyphens; punctuation stays in place.
    private static readonly Regex Words = new(@"\p{L}[\p{L}\p{M}]*(?:[-’'][\p{L}\p{M}]+)*", RegexOptions.NonBacktracking);
    public ExerciseDraft? Create(ExerciseMaterial material)
    {
        if (material.Sentence is not { } s) return null;
        var matches = Words.Matches(s.Original).ToArray();
        if (matches.Length < 2) return null;
        var word = matches[Random.Shared.Next(matches.Length)];
        var alternatives = ExerciseJson.Shuffle(matches.Select(x => x.Value).Concat(material.Words.Select(x => x.Word))
            .Where(x => x != word.Value).Distinct(StringComparer.Ordinal)).Take(3).Prepend(word.Value);
        var options = ExerciseJson.Shuffle(alternatives.Select(x => new ExerciseItem(Guid.NewGuid().ToString("N"), x)));
        return new("", ExerciseJson.Element(new GapExercisePrompt(s.Original[..word.Index], s.Original[(word.Index + word.Length)..], options, true)),
            ExerciseJson.Element(new GapAnswerKey(word.Value, options)));
    }
    public bool Evaluate(JsonElement key, JsonElement answer)
    {
        var k = ExerciseJson.Read<GapAnswerKey>(key);
        var a = ExerciseJson.Read<GapExerciseAnswer>(answer);
        if ((a.Text is null) == (a.OptionId is null)) throw new DomainException("Supply one optionId or text.");
        var text = a.OptionId is null ? ExerciseJson.Text(a.Text) : k.Options.SingleOrDefault(x => x.Id == a.OptionId)?.Text
            ?? throw new DomainException("Option was not issued.");
        return text == k.Text;
    }
}

public sealed class SentenceOrderHandler : IExerciseHandler
{
    public string Kind => "sentence-order";
    public ExerciseDraft? Create(ExerciseMaterial material)
    {
        if (material.Sentence is not { } s || s.LanguagePair.Split('-')[0] != "ba") return null;
        var parts = s.Original.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        var tokens = parts.Select(x => new ExerciseItem(Guid.NewGuid().ToString("N"), x)).ToArray();
        return new("", ExerciseJson.Element(new SentenceOrderPrompt(ExerciseJson.Shuffle(tokens))), ExerciseJson.Element(tokens));
    }
    public bool Evaluate(JsonElement key, JsonElement answer)
    {
        var tokens = ExerciseJson.Read<ExerciseItem[]>(key);
        var ids = ExerciseJson.Read<SentenceOrderAnswer>(answer).TokenIds;
        if (ids is null || ids.Count != tokens.Length || ids.Distinct().Count() != ids.Count || ids.Any(id => !tokens.Any(t => t.Id == id)))
            throw new DomainException("Use each issued token exactly once.");
        // Swapping identical words is linguistically the same answer, even with separate token identities.
        return ids.Select(id => tokens.Single(t => t.Id == id).Text).SequenceEqual(tokens.Select(t => t.Text));
    }
}

public sealed class WordMatchHandler : IExerciseHandler
{
    public string Kind => "word-match";
    public bool RequiresSentence => false;
    public ExerciseDraft? Create(ExerciseMaterial material)
    {
        var words = ExerciseJson.Shuffle(material.Words);
        var selected = new List<ExerciseWord>();
        foreach (var word in words)
        {
            if (selected.Any(x => x.Word == word.Word || x.Translation == word.Translation)) continue;
            selected.Add(word);
            if (selected.Count == 5) break;
        }
        if (selected.Count != 5) return null;
        var pairs = selected.Select(w => (Left: new ExerciseItem(Guid.NewGuid().ToString("N"), w.Word), Right: new ExerciseItem(Guid.NewGuid().ToString("N"), w.Translation))).ToArray();
        return new("", ExerciseJson.Element(new WordMatchPrompt(ExerciseJson.Shuffle(pairs.Select(x => x.Left)), ExerciseJson.Shuffle(pairs.Select(x => x.Right)), material.LanguagePair)),
            ExerciseJson.Element(pairs.Select(x => new WordPairAnswer(x.Left.Id, x.Right.Id)).ToArray()));
    }
    public bool Evaluate(JsonElement key, JsonElement answer)
    {
        var expected = ExerciseJson.Read<WordPairAnswer[]>(key);
        var pairs = ExerciseJson.Read<WordMatchAnswer>(answer).Pairs;
        if (pairs is null || pairs.Count != 5 || pairs.Any(p => p is null) || pairs.Select(p => p.LeftId).Distinct().Count() != 5 || pairs.Select(p => p.RightId).Distinct().Count() != 5 ||
            pairs.Any(p => !expected.Any(e => e.LeftId == p.LeftId) || !expected.Any(e => e.RightId == p.RightId)))
            throw new DomainException("Return five pairs using every issued word and translation exactly once.");
        return pairs.All(expected.Contains);
    }
}
