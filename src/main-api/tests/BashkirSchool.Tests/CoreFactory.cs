using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BashkirSchool.Application;
using BashkirSchool.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace BashkirSchool.Tests;

public sealed class ManualClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => Now;
}

public sealed class CoreFactory(int registrationLimit = 10, string? telegramKey = null, string? lmsKey = null, string? learningKind = null) : WebApplicationFactory<Program>
{
    public const string Password = "School.Test.Password123!";
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    public ManualClock Clock { get; } = new();
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        if (telegramKey is not null) builder.UseSetting("Telegram:InternalKey", telegramKey);
        if (lmsKey is not null) builder.UseSetting("Lms:InternalKey", lmsKey);
        builder.UseSetting("Database:InitializeOnStartup", "false");
        if (learningKind is not null)
            for (var i = 0; i < 6; i++) builder.UseSetting($"Learning:Kinds:{i}", learningKind);
        builder.UseSetting("Processing:BackgroundEnabled", "false");
        builder.UseSetting("Registration:PermitLimit", registrationLimit.ToString());
        builder.UseSetting("Jwt:Issuer", "school-tests");
        builder.UseSetting("Jwt:Audience", "school-test-client");
        builder.UseSetting("Jwt:SigningKey", "only-for-tests-a-long-signing-key-0123456789");
        builder.UseSetting("DataProtection:KeyDirectory", Path.Combine(Path.GetTempPath(), "bashkir-test-keys"));
        builder.ConfigureServices(services =>
        {
            connection.Open();
            services.RemoveAll<DbContextOptions<CoreDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<CoreDbContext>>();
            services.AddDbContext<CoreDbContext>(o => o.UseSqlite(connection).ReplaceService<IModelCustomizer, SqliteModelCustomizer>());
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    public async Task<HttpClient> Bootstrap()
    {
        var client = CreateClient();
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<CoreDbContext>().Database.EnsureCreatedAsync();
        await scope.ServiceProvider.GetRequiredService<SchoolAccounts>().BootstrapAsync("admin", Password, default);
        await Login(client, "admin");
        return client;
    }

    public static async Task Login(HttpClient client, string username, string password = Password)
    {
        var response = await client.PostAsJsonAsync("/auth/sign-in", new SignInRequest(username, password));
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<AccessTokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
    }

    public static async Task<T> Post<T>(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body, Json);
        if (!response.IsSuccessStatusCode) throw new Xunit.Sdk.XunitException($"{url}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    public static Task<LevelResponse> CurrentLevel(HttpClient client) => Post<LevelResponse>(client, "/api/learning/current", new { });
    public static string LevelAnswerUrl(LevelResponse level) => $"/api/learning/levels/{level.Id}/questions/{level.Question.Id}/answer";
    public static Task<LevelAnswerResponse> AnswerLevel(HttpClient client, LevelResponse level, object answer) =>
        Post<LevelAnswerResponse>(client, LevelAnswerUrl(level), new AnswerLevelRequest(level.Question.AttemptToken, ExerciseJson.Element(answer)));
    public static Task<ExpertDatasetReceipt> LearningContent(HttpClient admin, string text = "Һаумы", string translation = "Hello") =>
        Post<ExpertDatasetReceipt>(admin, "/api/expert/imports", new ExpertDatasetRow("test-learning", text, "ba-ru", translation, true, new SourceReference("test")));

    public async Task<(HttpClient Client, Guid Id, Guid ClassId)> Student(HttpClient admin, string suffix = "1")
    {
        var school = await Post<IdResponse>(admin, "/api/schools", new CreateSchoolRequest("School " + suffix));
        var teacher = await Post<AccountResponse>(admin, "/api/staff", new CreateStaffRequest("teacher" + suffix, "teacher", school.Id));
        await Activate(admin, teacher.Id);
        var teacherClient = CreateClient();
        await Login(teacherClient, teacher.Username);
        var studyClass = await Post<IdResponse>(teacherClient, "/api/classes", new CreateClassRequest(school.Id, "Class " + suffix));
        var student = await Post<AccountResponse>(teacherClient, $"/api/classes/{studyClass.Id}/students", new CreateStudentRequest("student" + suffix));
        await Activate(teacherClient, student.Id);
        var client = CreateClient();
        await Login(client, student.Username);
        return (client, student.Id, studyClass.Id);
    }

    public static async Task Activate(HttpClient caller, Guid accountId)
    {
        var link = await Post<PasswordSetupResponse>(caller, $"/api/accounts/{accountId}/password-link", new { });
        var complete = await caller.PostAsJsonAsync("/auth/password", new CompletePasswordRequest(accountId, link.Token, Password));
        complete.EnsureSuccessStatusCode();
    }

    public async Task<HttpClient> Producer(HttpClient admin)
    {
        var producer = await Post<AccountResponse>(admin, "/api/staff", new CreateStaffRequest("producer", "producer"));
        await Activate(admin, producer.Id);
        var client = CreateClient();
        await Login(client, producer.Username);
        return client;
    }
    public static async Task<DeliveryResponse> Deliver(HttpClient producer, string key = "one", string translation = "Hello")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/deliveries");
        request.Headers.Add("Idempotency-Key", key);
        request.Content = JsonContent.Create(new DeliverContentRequest("fragment-" + key, "Һаумы", "ba-ru", translation, new("document-1")));
        var response = await producer.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DeliveryResponse>())!;
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) connection.Dispose();
    }
}
public sealed record IdResponse(Guid Id);

// SQLite is a relational test adapter, not a claim about PostgreSQL transaction behavior.
public sealed class SqliteModelCustomizer(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(x => x.GetProperties()))
            if ((Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) == typeof(DateTimeOffset))
                property.SetValueConverter(new ValueConverter<DateTimeOffset, long>(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero)));
    }
}
