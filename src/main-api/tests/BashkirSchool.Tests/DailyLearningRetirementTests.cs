using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BashkirSchool.Application;
using BashkirSchool.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using static BashkirSchool.Tests.CoreFactory;

namespace BashkirSchool.Tests;

public sealed class DailyLearningRetirementTests
{
    [Fact]
    public async Task OnlyLevelLearningRoutesRemainAndOldRoutesCannotBypassProgress()
    {
        using var f = new CoreFactory(telegramKey: "retirement-key");
        using var admin = await f.Bootstrap();
        var student = await f.Student(admin);
        await LearningContent(admin);
        var level = await CurrentLevel(student.Client);
        Assert.Equal(1, level.Number);
        var questionId = level.Question.Id;
        foreach (var path in new[] { "/api/lessons/today", "/internal/telegram/101/lesson" })
            Assert.Equal(HttpStatusCode.NotFound, (await student.Client.GetAsync(path)).StatusCode);
        foreach (var path in new[] { "/api/lessons/today/boss", "/api/lessons/today/exercises",
            $"/api/exercises/{questionId}/attempt", $"/api/exercises/{questionId}/answer",
            "/internal/telegram/101/lesson/boss", "/internal/telegram/101/lesson/exercises",
            $"/internal/telegram/101/exercises/{questionId}/answer" })
            Assert.Equal(HttpStatusCode.NotFound, (await student.Client.PostAsJsonAsync(path, new { answer = "Hello" })).StatusCode);
        Assert.Equal(level.Question.AttemptToken, (await CurrentLevel(student.Client)).Question.AttemptToken);
        Assert.Equal(0, (await student.Client.GetFromJsonAsync<LevelProgressResponse>("/api/learning/progress"))!.Attempts);
        using var document = JsonDocument.Parse(await student.Client.GetStringAsync("/openapi/v1.json"));
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/learning/current", out _));
        Assert.False(paths.TryGetProperty("/api/lessons/today", out _));
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        foreach (var name in new[] { "LessonResponse", "ExerciseResponse", "SubmitAttemptRequest", "AttemptResponse", "IssueExerciseRequest", "SubmitExerciseAnswerRequest" })
            Assert.False(schemas.TryGetProperty(name, out _));
    }

    [Fact]
    public void RetiredEntitiesLeaveTheRuntimeModelWithoutDroppingHistoricalTables()
    {
        using var db = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>().UseNpgsql("Host=localhost;Database=model_only").Options);
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.DoesNotContain(db.Model.GetEntityTypes(), x => x.ClrType.Name is "DailyLesson" or "Exercise" or "ExerciseAttempt");
        var sql = db.GetService<IMigrator>().GenerateScript("20261114195743_PersonalLearningLevels");
        Assert.DoesNotContain("DROP TABLE", sql);
        Assert.DoesNotContain("DELETE FROM", sql);
        Assert.DoesNotContain("CREATE TABLE", sql);
    }
}
