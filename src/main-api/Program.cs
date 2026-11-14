using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
// The service owns its database and applies only the main API migration history.
var connection = builder.Configuration.GetConnectionString("Default") ?? throw new InvalidOperationException("ConnectionStrings:Default is required");
_ = builder.Configuration["INTERNAL_API_KEY"] ?? throw new InvalidOperationException("INTERNAL_API_KEY is required");
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connection,
    postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory_Main")));
builder.Services.AddControllers();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<CommunityService>();
builder.Services.AddScoped<TaskService>();
builder.Services.AddScoped<TelegramService>();
builder.Services.AddScoped<TranslationService>();

var app = builder.Build();
// Migrate before serving requests so controllers never see an outdated schema.
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DemoSeed.Apply(db);
}

// All HTTP endpoints are implemented by MVC controllers.
app.MapControllers();
app.Run();
