using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BashkirSchool.Application;
using BashkirSchool.Domain;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BashkirSchool.Infrastructure;

public sealed class AccountSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public long AccountVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
public sealed class RefreshCredential
{
    public string Hash { get; set; } = "";
    public Guid SessionId { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}

public sealed class Sessions(CoreDbContext db, IOptions<JwtSettings> settings, TimeProvider clock, IPermissionChecker permissions)
{
    public async Task<AccessTokenResponse> OpenAsync(Account account, CancellationToken ct, long? telegramId = null)
    {
        var now = clock.GetUtcNow();
        var session = new AccountSession { AccountId = account.Id, AccountVersion = account.SessionVersion, CreatedAt = now, ExpiresAt = now.AddDays(30) };
        db.Add(session);
        if (telegramId.HasValue) db.Add(new Microsoft.AspNetCore.Identity.IdentityUserToken<Guid>
        {
            UserId = account.Id, LoginProvider = "Telegram", Name = "session:" + session.Id,
            Value = telegramId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });
        var result = Issue(account, session);
        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task<AccessTokenResponse> RefreshAsync(string token, CancellationToken ct)
    {
        Guard.Text(token, "refreshToken", 200);
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var hash = Hash(token);
        var credential = await db.Set<RefreshCredential>().SingleOrDefaultAsync(x => x.Hash == hash, ct) ?? throw new AccessDeniedException();
        var session = await db.Set<AccountSession>().SingleAsync(x => x.Id == credential.SessionId, ct);
        var account = await db.Users.SingleAsync(x => x.Id == session.AccountId, ct);
        var now = clock.GetUtcNow();
        if (credential.UsedAt.HasValue)
        {
            session.RevokedAt = now;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            throw new AccessDeniedException();
        }
        if (!account.Active || account.SessionVersion != session.AccountVersion || session.RevokedAt.HasValue || session.ExpiresAt <= now)
            throw new AccessDeniedException();
        credential.UsedAt = now;
        var result = Issue(account, session);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<IReadOnlyList<SessionResponse>> ListAsync(Guid actor, CancellationToken ct)
    {
        await Require(actor, ct);
        return await db.Set<AccountSession>().AsNoTracking().Where(x => x.AccountId == actor).OrderByDescending(x => x.CreatedAt).Take(100)
            .Select(x => new SessionResponse(x.Id, x.CreatedAt, x.ExpiresAt, x.RevokedAt)).ToArrayAsync(ct);
    }

    public async Task RevokeAsync(Guid actor, Guid sessionId, CancellationToken ct)
    {
        await Require(actor, ct);
        var session = await db.Set<AccountSession>().SingleOrDefaultAsync(x => x.Id == sessionId && x.AccountId == actor, ct) ?? throw new MissingResourceException();
        session.RevokedAt ??= clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    private AccessTokenResponse Issue(Account account, AccountSession session)
    {
        var config = settings.Value; config.Validate();
        var now = clock.GetUtcNow();
        var expiry = now.AddMinutes(15) < session.ExpiresAt ? now.AddMinutes(15) : session.ExpiresAt;
        var refresh = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48));
        db.Add(new RefreshCredential { Hash = Hash(refresh), SessionId = session.Id });
        var jwt = new JwtSecurityToken(config.Issuer, config.Audience,
            [new Claim("sub", account.Id.ToString()), new Claim("sv", account.SessionVersion.ToString()), new Claim("sid", session.Id.ToString()), new Claim("jti", Guid.NewGuid().ToString())],
            now.UtcDateTime, expiry.UtcDateTime,
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config.SigningKey)), SecurityAlgorithms.HmacSha256));
        return new(new JwtSecurityTokenHandler().WriteToken(jwt), expiry, refresh, session.Id, session.ExpiresAt);
    }
    private async Task Require(Guid actor, CancellationToken ct)
    {
        if (!await permissions.HasAsync(actor, "sessions.manage", AccessScope.Own, actor, ct)) throw new AccessDeniedException();
    }
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
