using System.Text.Json;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramBotKit.Keyboards;

public sealed record ExerciseDraft(ExerciseView Exercise, string[] Selected, int? Left = null, int? LastUpdateId = null,
    Guid? LevelId = null, Guid? AttemptToken = null);

public static class TaskButtons
{
    public static InlineKeyboardMarkup Menu(string site = "https://bashperevod.ru") => Keyboard.Inline([
        Keyboard.Row(Keyboard.Callback("Текущий уровень", "nav", "level"), Keyboard.Callback("Босс", "nav", "boss")),
        Keyboard.Row(Keyboard.Callback("Профиль", "nav", "profile"), Keyboard.Callback("Переводы", "nav", "kinds")),
        Keyboard.Row(new InlineKeyboardButton("Рейтинг") { WebApp = new() { Url = AppUrl(site, "/leaderboard") } },
            new InlineKeyboardButton("Открыть сайт") { WebApp = new() { Url = AppUrl(site, "/") } })
    ]);

    private static string AppUrl(string site, string path)
    {
        var root = new Uri(site);
        if (root.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException("MINI_APP_URL must use HTTPS.");
        return new Uri(root, path + "?miniapp=1").AbsoluteUri;
    }
    public static InlineKeyboardMarkup Website(string site) => Keyboard.Inline([
        Keyboard.Row(new InlineKeyboardButton("Рейтинг") { WebApp = new() { Url = AppUrl(site, "/leaderboard") } }),
        Keyboard.Row(new InlineKeyboardButton("Открыть весь сайт") { WebApp = new() { Url = AppUrl(site, "/") } })
    ]);
    public static InlineKeyboardMarkup Continue(string action, string label = "Продолжить") => Keyboard.Inline([
        Keyboard.Row(Keyboard.Callback(label, "nav", action)),
        Keyboard.Row(Keyboard.Callback(action == "boss" ? "Текущий уровень" : "Босс", "nav", action == "boss" ? "level" : "boss"))
    ]);

    public static InlineKeyboardMarkup Kinds() => Keyboard.Inline(
        new[] {
                Keyboard.Row(Keyboard.Callback("Текущий уровень", "nav", "level")),
                Keyboard.Row(Keyboard.Callback("Босс", "nav", "boss")),
                Keyboard.Row(Keyboard.Callback("Предложить перевод", "review", "translation-create")),
                Keyboard.Row(Keyboard.Callback("Выбор перевода", "review", "translation-choice")),
                Keyboard.Row(Keyboard.Callback("Подтверждение перевода", "review", "translation-confirm")),
                Keyboard.Row(Keyboard.Callback("Продолжить уровень", "nav", "level"))
            });

    public static InlineKeyboardMarkup Exercise(ExerciseDraft draft)
    {
        var e = draft.Exercise;
        var rows = new List<IEnumerable<InlineKeyboardButton>>();
        InlineKeyboardButton Button(string label, string value) => Keyboard.Callback(label, "answer", (draft.AttemptToken ?? e.Id).ToString("N"), value);
        var p = e.Prompt;
        if (e.Kind == "missing-word")
            rows.AddRange(p.GetProperty("options").EnumerateArray().Select((x, i) => Keyboard.Row(Button(x.GetProperty("text").GetString()!, "g" + i))));
        if (e.Kind == "sentence-order")
            rows.AddRange(p.GetProperty("tokens").EnumerateArray().Select((x, i) => (Item: x, Index: i))
                .Where(x => !draft.Selected.Contains(x.Index.ToString()))
                .Select(x => Keyboard.Row(Button(x.Item.GetProperty("text").GetString()!, "t" + x.Index))));
        if (e.Kind == "word-match")
        {
            var side = draft.Left.HasValue ? "right" : "left";
            var used = draft.Selected.Select(pair => pair.Split(':')[draft.Left.HasValue ? 1 : 0]).ToArray();
            rows.AddRange(p.GetProperty(side).EnumerateArray().Select((x, i) => (Item: x, Index: i))
                .Where(x => !used.Contains(x.Index.ToString()))
                .Select(x => Keyboard.Row(Button(x.Item.GetProperty("text").GetString()!, (draft.Left.HasValue ? "r" : "l") + x.Index))));
        }
        if (e.Kind is "sentence-order" or "word-match")
        {
            var count = p.GetProperty(e.Kind == "sentence-order" ? "tokens" : "left").GetArrayLength();
            if (draft.Selected.Length == count) rows.Add(Keyboard.Row(Button("Отправить ответ", "send")));
            if (draft.Selected.Length > 0 || draft.Left.HasValue) rows.Add(Keyboard.Row(Button("Отменить последний выбор", "undo")));
        }
        rows.Add(Keyboard.Row(Keyboard.Callback("Виды заданий", "nav", "kinds")));
        return Keyboard.Inline(rows);
    }

    public static InlineKeyboardMarkup Review(ReviewView review, bool boss = false)
    {
        InlineKeyboardButton Button(string label, string value) => Keyboard.Callback(label, "vote", review.Id.ToString("N"), value);
        var rows = review.Kind switch
        {
            "translation-choice" => review.Options.Select((x, i) => Keyboard.Row(Button(x.Text, "v" + i))).ToList(),
            "translation-create" => [],
            _ => new List<IReadOnlyList<InlineKeyboardButton>> { Keyboard.Row(Button("Подтвердить перевод", "confirm")) }
        };
        if (review.AllowCustom) rows.Add(Keyboard.Row(Button("Свой перевод", "custom")));
        if (!boss) rows.Add(Keyboard.Row(Button("Пропустить перевод", "skip")));
        rows.Add(Keyboard.Row(Keyboard.Callback("Виды заданий", "nav", "kinds")));
        return Keyboard.Inline(rows);
    }

    public static string Progress(ExerciseDraft draft)
    {
        var p = draft.Exercise.Prompt;
        if (draft.Exercise.Kind == "sentence-order")
            return "\n\nВаше предложение: " + string.Join(' ', draft.Selected.Select(x => p.GetProperty("tokens")[int.Parse(x)].GetProperty("text").GetString()));
        if (draft.Exercise.Kind == "word-match")
        {
            var pairs = draft.Selected.Select(x => x.Split(':')).Select(x => p.GetProperty("left")[int.Parse(x[0])].GetProperty("text").GetString() + " — " + p.GetProperty("right")[int.Parse(x[1])].GetProperty("text").GetString());
            var selected = draft.Left.HasValue ? "\nВыберите перевод для: " + p.GetProperty("left")[draft.Left.Value].GetProperty("text").GetString() : "\nВыберите слово слева.";
            return "\n\nВаши пары:\n" + string.Join('\n', pairs) + selected;
        }
        return "";
    }

    public static ExerciseDraft Select(ExerciseDraft draft, string value)
    {
        if (value == "undo") return draft.Left.HasValue ? draft with { Left = null } : draft with { Selected = draft.Selected.SkipLast(1).ToArray() };
        if (value.Length < 2 || !int.TryParse(value[1..], out var index) || index < 0) throw new FormatException();
        var p = draft.Exercise.Prompt;
        if (value[0] == 't' && draft.Exercise.Kind == "sentence-order" && index < p.GetProperty("tokens").GetArrayLength() && !draft.Selected.Contains(index.ToString()))
            return draft with { Selected = [.. draft.Selected, index.ToString()] };
        if (draft.Exercise.Kind == "word-match")
        {
            if (value[0] == 'l' && !draft.Left.HasValue && index < p.GetProperty("left").GetArrayLength() && !draft.Selected.Any(x => x.Split(':')[0] == index.ToString()))
                return draft with { Left = index };
            if (value[0] == 'r' && draft.Left.HasValue && index < p.GetProperty("right").GetArrayLength() && !draft.Selected.Any(x => x.Split(':')[1] == index.ToString()))
                return draft with { Left = null, Selected = [.. draft.Selected, $"{draft.Left.Value}:{index}"] };
        }
        throw new FormatException();
    }

    public static object Answer(ExerciseDraft draft)
    {
        var text = draft.Exercise.Kind == "sentence-order"
            ? string.Join(' ', draft.Selected.Select(x => int.Parse(x) + 1))
            : string.Join(' ', draft.Selected.Select(x => x.Split(':')).Select(x => $"{int.Parse(x[0]) + 1}-{int.Parse(x[1]) + 1}"));
        return ExerciseChat.Answer(draft.Exercise, text);
    }
}
