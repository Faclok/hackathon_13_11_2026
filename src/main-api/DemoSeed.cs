using Microsoft.EntityFrameworkCore;

static class DemoSeed
{
    public static async Task Apply(AppDbContext db)
    {
        if (await db.Sources.AnyAsync()) return;
        await using var transaction = await db.Database.BeginTransactionAsync();
        var source = new SourceItem { Dataset = "demo", OriginalId = "starter", Title = "Демонстрационные фразы",
            Attribution = "Команда проекта", License = "Авторский демонстрационный материал" };
        db.Sources.Add(source);
        await db.SaveChangesAsync();
        var phrases = new (string Bashkir, string Russian)[] {
            ("Һаумыһығыҙ!", "Здравствуйте!"),
            ("Рәхмәт!", "Спасибо!"),
            ("Хәйерле иртә!", "Доброе утро!"),
            ("Һау булығыҙ!", "До свидания!"),
            ("Хәлдәр нисек?", "Как дела?"),
            ("Мин башҡорт телен өйрәнәм.", "Я изучаю башкирский язык.")
        };
        foreach (var (bashkir, russian) in phrases)
        {
            var fragment = new Fragment { SourceId = source.Id, TextBa = bashkir };
            db.Fragments.Add(fragment);
            await db.SaveChangesAsync();
            db.Candidates.Add(new TranslationCandidate { FragmentId = fragment.Id, Text = russian });
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
}
