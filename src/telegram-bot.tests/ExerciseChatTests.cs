using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramBotKit;
using TelegramBotKit.DependencyInjection;
using TelegramBotKit.Dispatching;
using TelegramBotKit.Fallbacks;

public sealed class ExerciseChatTests
{
    [Theory]
    [InlineData("translation-text")]
    [InlineData("audio-dictation")]
    [InlineData("audio-translation")]
    public void TextAnswerPreservesBashkirLettersAndPunctuation(string kind)
    {
        var exercise = View(kind, "{}");
        var answer = JsonSerializer.SerializeToElement(ExerciseChat.Answer(exercise, "Һаумы, донъя!"));
        Assert.Equal("Һаумы, донъя!", answer.GetProperty("text").GetString());
    }

    [Fact]
    public void NumberedAnswersUseIssuedIdentifiersForGapsOrderAndPairs()
    {
        var gap = View("missing-word", """{"before":"Һаумы, ","after":"!","allowCustom":true,"options":[{"id":"world","text":"донъя"},{"id":"school","text":"мәктәп"}]}""");
        Assert.Contains("1. донъя", ExerciseChat.Prompt(gap));
        Assert.Equal("school", JsonSerializer.SerializeToElement(ExerciseChat.Answer(gap, "2")).GetProperty("optionId").GetString());
        Assert.Equal("донъя", JsonSerializer.SerializeToElement(ExerciseChat.Answer(gap, "донъя")).GetProperty("text").GetString());
        var order = View("sentence-order", """{"tokens":[{"id":"b","text":"донъя"},{"id":"a","text":"Һаумы"}]}""");
        Assert.Equal(new[] { "a", "b" }, JsonSerializer.SerializeToElement(ExerciseChat.Answer(order, "2 1")).GetProperty("tokenIds").EnumerateArray().Select(x => x.GetString()));
        var match = View("word-match", """{"left":[{"id":"a","text":"бер"},{"id":"b","text":"ике"}],"right":[{"id":"x","text":"два"},{"id":"y","text":"один"}]}""");
        var pairs = JsonSerializer.SerializeToElement(ExerciseChat.Answer(match, "1-2 2-1")).GetProperty("pairs").EnumerateArray().ToArray();
        Assert.Equal("a", pairs[0].GetProperty("leftId").GetString()); Assert.Equal("y", pairs[0].GetProperty("rightId").GetString());
        Assert.Equal("b", pairs[1].GetProperty("leftId").GetString()); Assert.Equal("x", pairs[1].GetProperty("rightId").GetString());
    }

    [Theory]
    [InlineData("1 1")]
    [InlineData("0 2")]
    [InlineData("1 3")]
    [InlineData("1")]
    [InlineData("one two")]
    public void InvalidOrderDoesNotSubmitAnAnswer(string text)
    {
        var order = View("sentence-order", """{"tokens":[{"id":"a","text":"Һаумы"},{"id":"b","text":"донъя"}]}""");
        Assert.Throws<FormatException>(() => ExerciseChat.Answer(order, text));
    }

    [Theory]
    [InlineData("1-1 2-1")]
    [InlineData("1-1 1-2")]
    [InlineData("1-3 2-1")]
    [InlineData("1-1")]
    [InlineData("1 2")]
    public void InvalidPairsDoNotSubmitAnAnswer(string text)
    {
        var match = View("word-match", """{"left":[{"id":"a","text":"бер"},{"id":"b","text":"ике"}],"right":[{"id":"x","text":"один"},{"id":"y","text":"два"}]}""");
        Assert.Throws<FormatException>(() => ExerciseChat.Answer(match, text));
    }

    [Fact]
    public async Task TelegramBotKitDispatchesSlashCommandsAndPlainAnswersToConversation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTelegramBotKit(options => options.Token = "123456789:ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijk");
        var recorder = new RecordingConversation();
        services.AddSingleton<IDefaultMessageHandler>(recorder);
        await using var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<IUpdateDispatcher>();
        foreach (var text in new[] { "/start code", "/profile", "Задания на сегодня", "Һаумы!" })
            await dispatcher.DispatchAsync(new Update { Id = 1, Message = new Message { Id = 1, Text = text, Chat = new Chat { Id = 101, Type = ChatType.Private }, From = new User { Id = 101, FirstName = "Student" } } }, default);
        Assert.Equal(new[] { "/start code", "/profile", "Задания на сегодня", "Һаумы!" }, recorder.Messages);
    }

    private static ExerciseView View(string kind, string json) => new(Guid.NewGuid(), "", false, kind, 1, JsonDocument.Parse(json).RootElement.Clone());
    private sealed class RecordingConversation : IDefaultMessageHandler
    {
        public List<string> Messages { get; } = [];
        public Task HandleAsync(Message message, BotContext context) { Messages.Add(message.Text!); return Task.CompletedTask; }
    }
}
