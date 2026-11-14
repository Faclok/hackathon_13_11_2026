using BashkirSchool.Domain;
using BashkirSchool.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BashkirSchool.Tests;

public sealed class ExerciseMigrationTests
{
    [PostgresFact]
    public async Task UpgradePreservesLegacyExerciseAndAllowsVocabularyWithoutASentence()
    {
        await using var database = await TestDatabase.Create();
        await using var db = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>().UseNpgsql(database.ConnectionString).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261114100124_IndependentParticipants");
        var account = new Account { Id = Guid.NewGuid(), UserName = "migration-participant" };
        var participant = new Participant { Id = account.Id };
        var lesson = new DailyLesson { ParticipantId = participant.Id, Date = new DateOnly(2026, 11, 14) };
        var fragment = Fragment.Create(account.Id, "legacy", "Һаумы", "ba-ru", "{}");
        var translationCase = TranslationCase.Open(fragment.Id, Guid.NewGuid(), DateTimeOffset.UtcNow);
        var variant = TranslationVariant.Create(translationCase.Id, "Здравствуйте", true, translationCase.ShownVariantId);
        var policy = new DistributionPolicy { ChangedBy = account.Id, Version = 1 };
        var resolution = ResolutionRevision.Create(translationCase.Id, 1, ResolutionKind.Community, variant.Id, [variant.Id], [], policy.Id, null, "{}", null, DateTimeOffset.UtcNow);
        db.AddRange(account, participant, fragment, translationCase, variant, policy, resolution);
        await db.SaveChangesAsync();
        // This fixture targets the historical schema, before later lesson columns exist.
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"DailyLesson\" (\"Id\", \"ParticipantId\", \"Date\") VALUES ({lesson.Id}, {lesson.ParticipantId}, {lesson.Date})");
        var exerciseId = Guid.NewGuid(); var key = "[\"Здравствуйте\"]";
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Exercise\" (\"Id\", \"LessonId\", \"CaseId\", \"ResolutionId\", \"Original\", \"AcceptedTextsJson\") VALUES ({exerciseId}, {lesson.Id}, {translationCase.Id}, {resolution.Id}, {fragment.Text}, {key})");
        await migrator.MigrateAsync(); db.ChangeTracker.Clear();
        var exercise = await db.Set<Exercise>().SingleAsync();
        Assert.Equal(exerciseId, exercise.Id); Assert.Equal("translation-text", exercise.Kind);
        Assert.Equal("{}", exercise.AnswerKeyJson); Assert.Equal(key, exercise.AcceptedTextsJson);
        Assert.Equal("Һаумы", exercise.Original); Assert.Equal(1, exercise.SchemaVersion);
        db.Add(new Exercise { LessonId = lesson.Id, Kind = "word-match", CaseId = null, ResolutionId = null });
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.Set<Exercise>().CountAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
