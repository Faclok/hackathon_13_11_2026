using Telegram.Bot.Types.ReplyMarkups;

public sealed record ReviewView(Guid Id, Guid CaseId, Guid VariantId, string Original, string Translation,
    DateTimeOffset ExpiresAt, string Kind, ReviewOption[] Options, bool AllowCustom);
public sealed record ReviewOption(Guid Id, string Text);
public sealed record ReviewAnswer(string Action, string? Correction = null, Guid? SelectedVariantId = null);
public sealed record ReviewDraft(ReviewView Assignment, bool Custom = false);

public static class ReviewChat
{
    public static string Prompt(ReviewView review) => review.Kind == "translation-choice"
        ? $"Выберите перевод предложения:\n\n{review.Original}\n\n" +
          string.Join('\n', review.Options.Select((x, i) => $"{i + 1}. {x.Text}")) +
          "\n\nНажмите номер варианта. Чтобы написать свой перевод, нажмите «Свой перевод»."
        : $"Проверьте перевод:\n\n{review.Original}\n\nПредложенный перевод:\n{review.Translation}\n\nПодтвердите его или отправьте свой перевод.";

    public static ReplyKeyboardMarkup Keyboard(ReviewView review)
    {
        var choices = review.Kind == "translation-choice"
            ? review.Options.Select((_, i) => (i + 1).ToString()).ToArray()
            : ["Подтвердить перевод"];
        return ExerciseChat.Keyboard(choices.Concat(review.AllowCustom ? new[] { "Свой перевод" } : [])
            .Append("Пропустить перевод").Append("Виды заданий"));
    }

    public static ReviewAnswer Answer(ReviewView review, string text, bool custom)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new FormatException();
        if (custom) return review.AllowCustom ? new("Correct", text) : throw new FormatException();
        if (text == "Пропустить перевод") return new("Skip");
        if (review.Kind == "translation-confirm" && (text == "Подтвердить перевод" || text.Equals("да", StringComparison.OrdinalIgnoreCase))) return new("Confirm");
        if (review.Kind == "translation-choice" && int.TryParse(text, out var option))
        {
            if (option < 1 || option > review.Options.Length) throw new FormatException();
            return new("Confirm", SelectedVariantId: review.Options[option - 1].Id);
        }
        return review.AllowCustom ? new("Correct", text) : throw new FormatException();
    }
}
