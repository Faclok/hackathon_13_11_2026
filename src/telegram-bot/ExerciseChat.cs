using System.Text.Json;
using Telegram.Bot.Types.ReplyMarkups;

public static class ExerciseChat
{
    public static readonly IReadOnlyDictionary<string, string> Kinds = new Dictionary<string, string>
    {
        ["Перевод текста"] = "translation-text", ["Сопоставление слов"] = "word-match",
        ["Аудиодиктант"] = "audio-dictation", ["Перевод аудио"] = "audio-translation",
        ["Пропущенное слово"] = "missing-word", ["Сборка предложения"] = "sentence-order"
    };

    public static ReplyKeyboardMarkup Keyboard(IEnumerable<string> buttons) => new(buttons
        .Concat(["Профиль", "Задания на сегодня"]).Chunk(2).Select(row => row.Select(x => new KeyboardButton(x)))) { ResizeKeyboard = true };

    public static ReplyKeyboardMarkup KindsKeyboard() => Keyboard(Kinds.Keys.Concat(["Выбор перевода", "Подтверждение перевода", "Продолжить урок"]));

    public static ReplyKeyboardMarkup Keyboard(ExerciseView exercise) => Keyboard(
        (exercise.Kind == "missing-word"
            ? Enumerable.Range(1, exercise.Prompt.GetProperty("options").GetArrayLength()).Select(x => x.ToString())
            : Enumerable.Empty<string>()).Append("Виды заданий"));

    public static string Prompt(ExerciseView exercise)
    {
        var p = exercise.Prompt;
        return exercise.Kind switch
        {
            "translation-text" => "Переведите текст:\n" + p.GetProperty("text").GetString() + "\n\nОтправьте перевод сообщением.",
            "audio-dictation" => "Прослушайте аудио и запишите услышанный текст сообщением.",
            "audio-translation" => "Прослушайте аудио и отправьте перевод сообщением.",
            "missing-word" => $"Заполните пропуск:\n{p.GetProperty("before").GetString()} ___ {p.GetProperty("after").GetString()}\n\n{Items(p, "options")}\nОтправьте номер варианта или впишите слово.",
            "sentence-order" => $"Соберите предложение:\n{Items(p, "tokens")}\nОтправьте номера слов по порядку через пробел, например: 3 1 2.",
            "word-match" => $"Сопоставьте слова:\n{Items(p, "left")}\n\nПереводы:\n{Items(p, "right")}\nОтправьте пары номеров, например: 1-3 2-1 3-2.",
            _ => "Тип задания пока не поддерживается."
        };
    }

    public static object Answer(ExerciseView exercise, string text)
    {
        var p = exercise.Prompt;
        switch (exercise.Kind)
        {
            case "translation-text": case "audio-dictation": case "audio-translation": return new { text };
            case "missing-word":
                if (int.TryParse(text, out var option)) return new { optionId = Id(p, "options", option) };
                if (!p.GetProperty("allowCustom").GetBoolean()) throw new FormatException();
                return new { text };
            case "sentence-order":
                var indices = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Number).ToArray();
                if (indices.Length != p.GetProperty("tokens").GetArrayLength() || indices.Distinct().Count() != indices.Length) throw new FormatException();
                return new { tokenIds = indices.Select(i => Id(p, "tokens", i)).ToArray() };
            case "word-match":
                var pairs = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(pair =>
                {
                    var values = pair.Split('-');
                    if (values.Length != 2) throw new FormatException();
                    return new { leftId = Id(p, "left", Number(values[0])), rightId = Id(p, "right", Number(values[1])) };
                }).ToArray();
                if (pairs.Length != p.GetProperty("left").GetArrayLength() || pairs.Select(x => x.leftId).Distinct().Count() != pairs.Length || pairs.Select(x => x.rightId).Distinct().Count() != pairs.Length) throw new FormatException();
                return new { pairs };
            default: throw new FormatException();
        }
    }

    private static int Number(string value) => int.TryParse(value, out var number) ? number : throw new FormatException();
    private static string Id(JsonElement prompt, string name, int number)
    {
        var items = prompt.GetProperty(name);
        if (number < 1 || number > items.GetArrayLength()) throw new FormatException();
        return items[number - 1].GetProperty("id").GetString()!;
    }
    private static string Items(JsonElement prompt, string name) => string.Join('\n', prompt.GetProperty(name).EnumerateArray().Select((x, i) => $"{i + 1}. {x.GetProperty("text").GetString()}"));
}
