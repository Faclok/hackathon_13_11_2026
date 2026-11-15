using Microsoft.Extensions.Configuration;
using Telegram.Bot;
using Telegram.Bot.Types;

public static class BotPresentation
{
    public static async Task ApplyAsync(ITelegramBotClient telegram, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var settings = configuration.GetSection("BotPresentation");
        var name = settings["Name"] ?? "Башкирская языковая школа";
        var description = settings["Description"] ??
            "Һаумы! Изучайте башкирский язык вместе с нами 🌿\n\n" +
            "Проходите уровни, слушайте аудио, тренируйте переводы и побеждайте боссов. " +
            "Ваш прогресс общий с сайтом bashperevod.ru.\n\n" +
            "Нажмите /start, чтобы начать. Для привязки аккаунта откройте профиль на сайте.";
        var shortDescription = settings["ShortDescription"] ??
            "Башкирский язык: уроки, аудио, переводы и боссы 🌿 bashperevod.ru";
        var languages = new[] { "" }.Concat(settings.GetSection("LanguageCodes").Get<string[]>() ?? ["ru", "ba", "en"])
            .Distinct(StringComparer.Ordinal).ToArray();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64 || description.Length > 512 || shortDescription.Length > 120)
            throw new InvalidOperationException("BotPresentation: Name must contain 1–64 characters, Description at most 512, ShortDescription at most 120.");
        if (languages.Any(language => language.Length != 0 && (language.Length != 2 || language.Any(c => c is < 'a' or > 'z'))))
            throw new InvalidOperationException("BotPresentation:LanguageCodes must contain two-letter lowercase language codes.");

        // Keep the asset in the assembly so switching tokens or deploying Docker needs no website download.
        await using var avatar = typeof(BotPresentation).Assembly.GetManifestResourceStream("BotAvatar.jpg")
            ?? throw new InvalidOperationException("The embedded bot avatar is missing.");
        BotCommandScope[] scopes = [new BotCommandScopeDefault(), new BotCommandScopeAllPrivateChats(),
            new BotCommandScopeAllGroupChats(), new BotCommandScopeAllChatAdministrators()];
        foreach (var language in languages)
        {
            foreach (var scope in scopes)
                await telegram.DeleteMyCommands(scope, language, cancellationToken);
        }

        foreach (var language in languages)
        {
            await telegram.SetMyName(name, language, cancellationToken);
            await telegram.SetMyDescription(description, language, cancellationToken);
            await telegram.SetMyShortDescription(shortDescription, language, cancellationToken);
            await telegram.SetMyCommands([
                new() { Command = "start", Description = "Начать и открыть главное меню" },
                new() { Command = "profile", Description = "Мой профиль и прогресс" },
                new() { Command = "level", Description = "Продолжить изучение языка" },
                new() { Command = "boss", Description = "Испытание за каждые пять уровней" },
                new() { Command = "rating", Description = "Рейтинг учеников" },
                new() { Command = "site", Description = "Открыть школу в Mini App" },
                new() { Command = "tasks", Description = "Переводы и проверка ответов" },
                new() { Command = "cancel", Description = "Вернуться в главное меню" },
                new() { Command = "help", Description = "Помощь и главное меню" }
            ], languageCode: language, cancellationToken: cancellationToken);
        }
        await telegram.SetChatMenuButton(menuButton: new MenuButtonCommands(), cancellationToken: cancellationToken);
        await telegram.SetMyProfilePhoto(new InputProfilePhotoStatic { Photo = InputFile.FromStream(avatar, "school-logo.jpg") }, cancellationToken);
    }
}
