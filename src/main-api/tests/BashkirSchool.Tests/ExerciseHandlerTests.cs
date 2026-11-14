using System.Text.Json;
using BashkirSchool.Application;
using BashkirSchool.Domain;

namespace BashkirSchool.Tests;

public sealed class ExerciseHandlerTests
{
    private static ExerciseMaterial Material(string text = "Мин мәктәпкә барам.", string[]? accepted = null, Guid? audio = null) =>
        new(new(Guid.NewGuid(), accepted is null ? null : Guid.NewGuid(), text, "ba-ru", accepted ?? [], audio), [], "ba-ru");
    private static JsonElement Answer<T>(T value) => ExerciseJson.Element(value);

    [Fact]
    public void WordMatchRequiresFiveUnambiguousPairsAndChecksBijection()
    {
        var handler = new WordMatchHandler();
        var words = Enumerable.Range(0, 5).Select(i => new ExerciseWord(Guid.NewGuid(), "word" + i, "translation" + i)).ToArray();
        Assert.Null(handler.Create(new(null, words[..4], "ba-ru")));
        var draft = handler.Create(new(null, words, "ba-ru"))!;
        var prompt = ExerciseJson.Read<WordMatchPrompt>(draft.Prompt);
        Assert.Equal(5, prompt.Left.Count); Assert.Equal(5, prompt.Right.Count);
        var pairs = prompt.Left.Select(l => new WordPairAnswer(l.Id, prompt.Right.Single(r => r.Text == "translation" + l.Text[^1]).Id)).ToArray();
        Assert.True(handler.Evaluate(draft.AnswerKey, Answer(new WordMatchAnswer(pairs.Reverse().ToArray()))));
        var wrong = pairs.ToArray();
        wrong[0] = wrong[0] with { RightId = pairs[1].RightId }; wrong[1] = wrong[1] with { RightId = pairs[0].RightId };
        Assert.False(handler.Evaluate(draft.AnswerKey, Answer(new WordMatchAnswer(wrong))));
        Assert.Throws<DomainException>(() => handler.Evaluate(draft.AnswerKey, Answer(new WordMatchAnswer([pairs[0], pairs[0], pairs[2], pairs[3], pairs[4]]))));
        var ambiguous = words.Select(w => w with { Translation = "same" }).ToArray();
        Assert.Null(handler.Create(new(null, ambiguous, "ba-ru")));
    }

    [Fact]
    public void DictationUsesOriginalWithoutTranslationAndHidesItFromPrompt()
    {
        var handler = new DictationHandler();
        Assert.Null(handler.Create(Material()));
        var draft = handler.Create(Material(audio: Guid.NewGuid()))!;
        Assert.Equal("", draft.Original);
        Assert.DoesNotContain("Мин", draft.Prompt.GetRawText());
        Assert.True(handler.Evaluate(draft.AnswerKey, Answer(new TextExerciseAnswer("Мин мәктәпкә барам."))));
        Assert.False(handler.Evaluate(draft.AnswerKey, Answer(new TextExerciseAnswer("Я иду в школу."))));
    }

    [Fact]
    public void AudioTranslationRequiresAudioAndAcceptedResolutionAndAcceptsSynonym()
    {
        var handler = new AudioTranslationHandler();
        Assert.Null(handler.Create(Material(audio: Guid.NewGuid())));
        Assert.Null(handler.Create(Material(accepted: ["Я иду в школу."])));
        var draft = handler.Create(Material(accepted: ["Я иду в школу.", "Я направляюсь в школу."], audio: Guid.NewGuid()))!;
        Assert.Equal("", draft.Original);
        Assert.DoesNotContain("школу", draft.Prompt.GetRawText());
        Assert.True(handler.Evaluate(draft.AnswerKey, Answer(new TextExerciseAnswer("Я направляюсь в школу."))));
        Assert.False(handler.Evaluate(draft.AnswerKey, Answer(new TextExerciseAnswer("Мин мәктәпкә барам."))));
    }

    [Fact]
    public void MissingWordAcceptsChoiceOrTextAndRejectsForeignOrMixedInput()
    {
        var handler = new MissingWordHandler();
        Assert.Null(handler.Create(Material("Һаумы")));
        var draft = handler.Create(Material())!;
        var prompt = ExerciseJson.Read<GapExercisePrompt>(draft.Prompt);
        var key = ExerciseJson.Read<GapAnswerKey>(draft.AnswerKey);
        Assert.Equal("Мин мәктәпкә барам.", prompt.Before + key.Text + prompt.After);
        Assert.Equal(3, prompt.Options.Count);
        Assert.True(handler.Evaluate(draft.AnswerKey, Answer(new GapExerciseAnswer(Text: key.Text))));
        Assert.True(handler.Evaluate(draft.AnswerKey, Answer(new GapExerciseAnswer(OptionId: prompt.Options.Single(x => x.Text == key.Text).Id))));
        Assert.False(handler.Evaluate(draft.AnswerKey, Answer(new GapExerciseAnswer(Text: "бүтән"))));
        Assert.Throws<DomainException>(() => handler.Evaluate(draft.AnswerKey, Answer(new GapExerciseAnswer(OptionId: "foreign"))));
        Assert.Throws<DomainException>(() => handler.Evaluate(draft.AnswerKey, Answer(new GapExerciseAnswer(key.Text, prompt.Options[0].Id))));
    }

    [Fact]
    public void SentenceOrderChecksAllTokensAndAllowsInterchangeableRepeatedWords()
    {
        var handler = new SentenceOrderHandler();
        var draft = handler.Create(Material("мин мин барам"))!;
        var prompt = ExerciseJson.Read<SentenceOrderPrompt>(draft.Prompt);
        Assert.Equal(3, prompt.Tokens.Select(x => x.Id).Distinct().Count());
        var repeated = prompt.Tokens.Where(x => x.Text == "мин").Select(x => x.Id).ToArray();
        var last = prompt.Tokens.Single(x => x.Text == "барам").Id;
        Assert.True(handler.Evaluate(draft.AnswerKey, Answer(new SentenceOrderAnswer([repeated[1], repeated[0], last]))));
        Assert.False(handler.Evaluate(draft.AnswerKey, Answer(new SentenceOrderAnswer([last, repeated[0], repeated[1]]))));
        Assert.Throws<DomainException>(() => handler.Evaluate(draft.AnswerKey, Answer(new SentenceOrderAnswer([repeated[0], repeated[0], last]))));
        Assert.Throws<DomainException>(() => handler.Evaluate(draft.AnswerKey, Answer(new SentenceOrderAnswer([repeated[0], repeated[1], "foreign"]))));
        Assert.Null(handler.Create(Material("Һаумы")));
    }

    [Fact]
    public void RegistryAcceptsNewKindAndVersionsWithoutWorkflowChanges()
    {
        var registry = new ExerciseRegistry([new FutureHandler()]);
        var handler = registry.Get("future-kind", 2);
        Assert.Equal(2, Assert.Single(registry.Kinds).SchemaVersion);
        Assert.Same(handler, registry.Latest("future-kind"));
        Assert.True(handler.Evaluate(Answer(new { }), Answer(new { })));
        Assert.Throws<DomainException>(() => registry.Get("future-kind", 1));
        Assert.Throws<DomainException>(() => registry.Get("unknown"));
        Assert.Throws<ArgumentException>(() => new ExerciseRegistry([new FutureHandler(), new FutureHandler()]));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"text\":null}")]
    [InlineData("{\"text\":\"\"}")]
    [InlineData("{\"text\":\"перевод\",\"recognized\":true}")]
    [InlineData("{\"text\":\"перевод\",\"text\":\"другое\"}")]
    public void TextAnswerRejectsMalformedOrForgedFields(string json)
    {
        var handler = new TranslationTextHandler();
        using var answer = JsonDocument.Parse(json);
        Assert.Throws<DomainException>(() => handler.Evaluate(Answer(new[] { "перевод" }), answer.RootElement));
    }

    private sealed class FutureHandler : IExerciseHandler
    {
        public string Kind => "future-kind";
        public int SchemaVersion => 2;
        public ExerciseDraft? Create(ExerciseMaterial material) => null;
        public bool Evaluate(JsonElement answerKey, JsonElement answer) => true;
    }
}
