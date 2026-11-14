using BashkirSchool.Domain;
using BashkirSchool.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BashkirSchool.Tests;

public sealed class ExerciseMigrationTests
{
    [PostgresFact]
    public async Task RetirementPreservesDailyLearningArchiveWithoutRuntimeEntities()
    {
        await using var database = await TestDatabase.Create();
        await using var db = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>().UseNpgsql(database.ConnectionString).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261114195743_PersonalLearningLevels");
        var account = new Account { Id = Guid.NewGuid(), UserName = "migration-participant" };
        var participant = new Participant { Id = account.Id };
        var lessonId = Guid.NewGuid(); var lessonDate = new DateOnly(2026, 11, 14);
        var fragment = Fragment.Create(account.Id, "legacy", "Һаумы", "ba-ru", "{}");
        var translationCase = TranslationCase.Open(fragment.Id, Guid.NewGuid(), DateTimeOffset.UtcNow);
        var variant = TranslationVariant.Create(translationCase.Id, "Здравствуйте", true, translationCase.ShownVariantId);
        var policy = new DistributionPolicy { ChangedBy = account.Id, Version = 1 };
        var resolution = ResolutionRevision.Create(translationCase.Id, 1, ResolutionKind.Community, variant.Id, [variant.Id], [], policy.Id, null, "{}", null, DateTimeOffset.UtcNow);
        db.AddRange(account, participant, fragment, translationCase, variant, policy, resolution);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"DailyLesson\" (\"Id\", \"ParticipantId\", \"Date\") VALUES ({lessonId}, {participant.Id}, {lessonDate})");
        var exerciseId = Guid.NewGuid(); var key = "[\"Здравствуйте\"]";
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Exercise\" (\"Id\", \"LessonId\", \"CaseId\", \"ResolutionId\", \"Original\", \"AcceptedTextsJson\") VALUES ({exerciseId}, {lessonId}, {translationCase.Id}, {resolution.Id}, {fragment.Text}, {key})");
        await migrator.MigrateAsync(); db.ChangeTracker.Clear();
        Assert.Equal(key, await db.Database.SqlQuery<string>($"SELECT \"AcceptedTextsJson\" AS \"Value\" FROM \"Exercise\" WHERE \"Id\" = {exerciseId}").SingleAsync());
        Assert.Equal("Һаумы", await db.Database.SqlQuery<string>($"SELECT \"Original\" AS \"Value\" FROM \"Exercise\" WHERE \"Id\" = {exerciseId}").SingleAsync());
        Assert.Equal(lessonId, await db.Database.SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM \"DailyLesson\" WHERE \"Id\" = {lessonId}").SingleAsync());
        Assert.DoesNotContain(db.Model.GetEntityTypes(), x => x.ClrType.Name is "DailyLesson" or "Exercise" or "ExerciseAttempt");
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
