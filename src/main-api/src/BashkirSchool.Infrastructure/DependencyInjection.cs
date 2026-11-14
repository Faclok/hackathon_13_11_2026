using System.Security.Claims;
using System.Text;
using BashkirSchool.Application;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace BashkirSchool.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCoreInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<CoreDbContext>(o => o.UseNpgsql(configuration.GetConnectionString("Core")
            ?? "Host=localhost;Database=bashkir_school;Username=postgres"));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IPermissionChecker, PermissionChecker>();
        services.AddScoped<IContentIntake, ContentIntake>();
        services.AddScoped<IReviewWorkflow, ReviewWorkflow>();
        services.AddScoped<SchoolAccounts>();
        services.AddScoped<IndependentAccounts>();
        services.AddScoped<Sessions>();
        services.AddScoped<SchoolDirectory>();
        services.AddScoped<CaseEvidence>();
        services.AddScoped<ExpertReviews>();
        services.AddScoped<TranslationExport>();
        services.AddScoped<ResolutionProcessor>();
        services.AddScoped<LearningWorkflow>();
        if (configuration.GetValue("Processing:BackgroundEnabled", true)) services.AddHostedService<ResolutionWorker>();
        services.AddIdentityCore<Account>(o =>
        {
            o.User.RequireUniqueEmail = false;
            o.Password.RequiredLength = 12;
            o.Lockout.MaxFailedAccessAttempts = 5;
            o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddEntityFrameworkStores<CoreDbContext>().AddDefaultTokenProviders();
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(24));
        var protection = services.AddDataProtection().SetApplicationName("BashkirSchool.Core");
        if (configuration["DataProtection:KeyDirectory"] is { Length: > 0 } keyDirectory)
        {
            protection.PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
            if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
        }
        else protection.PersistKeysToDbContext<CoreDbContext>();
        services.Configure<JwtSettings>(configuration.GetSection("Jwt"));
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            var jwt = configuration.GetSection("Jwt").Get<JwtSettings>() ?? new();
            jwt.Validate();
            o.MapInboundClaims = false;
            o.TokenValidationParameters = new()
            {
                ValidateIssuer = true, ValidIssuer = jwt.Issuer,
                ValidateAudience = true, ValidAudience = jwt.Audience,
                ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ClockSkew = TimeSpan.Zero, NameClaimType = "sub"
            };
            o.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    if (!Guid.TryParse(context.Principal?.FindFirstValue("sub"), out var id) ||
                        !long.TryParse(context.Principal?.FindFirstValue("sv"), out var version) ||
                        !Guid.TryParse(context.Principal?.FindFirstValue("sid"), out var sessionId))
                    { context.Fail("Invalid subject."); return; }
                    var db = context.HttpContext.RequestServices.GetRequiredService<CoreDbContext>();
                    if (!await db.Users.AsNoTracking().AnyAsync(x => x.Id == id && x.Active && x.SessionVersion == version, context.HttpContext.RequestAborted))
                    { context.Fail("Account or session revoked."); return; }
                    var now = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
                    if (!await db.Set<AccountSession>().AsNoTracking().AnyAsync(x => x.Id == sessionId && x.AccountId == id && x.RevokedAt == null && x.ExpiresAt > now, context.HttpContext.RequestAborted))
                        context.Fail("Session revoked or expired.");
                }
            };
        });
        services.AddAuthorization();
        return services;
    }
}
