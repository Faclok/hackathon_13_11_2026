using System.Globalization;
using System.Security.Cryptography;
using BashkirSchool.Application;
using BashkirSchool.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace BashkirSchool.Infrastructure;

public sealed class TelegramStudents(CoreDbContext db, IPermissionChecker permissions, TimeProvider clock)
{
    private const string Provider = "Telegram";

    public async Task<TelegramLinkResponse> IssueLinkAsync(Guid actor, CancellationToken ct)
    {
        await RequireStudent(actor, ct);
        var code = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var expires = clock.GetUtcNow().AddMinutes(10);
        var tokens = db.Set<IdentityUserToken<Guid>>();
        var pending = await tokens.SingleOrDefaultAsync(x => x.UserId == actor && x.LoginProvider == Provider && x.Name == "link", ct);
        if (pending is null) { pending = new() { UserId = actor, LoginProvider = Provider, Name = "link" }; tokens.Add(pending); }
        pending.Value = Hash(code) + ":" + expires.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        await db.SaveChangesAsync(ct);
        return new(code, expires);
    }

    public async Task LinkAsync(long telegramId, string code, CancellationToken ct)
    {
        if (telegramId <= 0 || string.IsNullOrWhiteSpace(code) || code.Length > 64) throw new DomainException("Invalid link code.");
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var prefix = Hash(code) + ":";
        var token = await db.Set<IdentityUserToken<Guid>>().SingleOrDefaultAsync(x => x.LoginProvider == Provider && x.Name == "link" && x.Value != null && x.Value.StartsWith(prefix), ct)
            ?? throw new DomainException("Invalid or consumed link code.");
        if (!long.TryParse(token.Value![prefix.Length..], out var expires) || clock.GetUtcNow().ToUnixTimeSeconds() >= expires)
            throw new DomainException("Link code expired.");
        await RequireStudent(token.UserId, ct);
        var key = telegramId.ToString(CultureInfo.InvariantCulture);
        var logins = db.Set<IdentityUserLogin<Guid>>();
        var existing = await logins.SingleOrDefaultAsync(x => x.LoginProvider == Provider && x.ProviderKey == key, ct);
        if (existing is not null && existing.UserId != token.UserId) throw new ConflictException("Telegram is already linked.");
        if (await logins.AnyAsync(x => x.LoginProvider == Provider && x.UserId == token.UserId && x.ProviderKey != key, ct))
            throw new ConflictException("Unlink the previous Telegram account first.");
        if (existing is null) logins.Add(new() { LoginProvider = Provider, ProviderKey = key, UserId = token.UserId, ProviderDisplayName = "Telegram" });
        db.Remove(token);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task UnlinkAsync(Guid actor, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var logins = await db.Set<IdentityUserLogin<Guid>>().Where(x => x.UserId == actor && x.LoginProvider == Provider).ToArrayAsync(ct);
        var tokens = await db.Set<IdentityUserToken<Guid>>().Where(x => x.UserId == actor && x.LoginProvider == Provider).ToArrayAsync(ct);
        var sessionIds = tokens.Where(x => x.Name.StartsWith("session:", StringComparison.Ordinal))
            .Select(x => Guid.TryParse(x.Name[8..], out var id) ? id : Guid.Empty).ToArray();
        var sessions = await db.Set<AccountSession>().Where(x => x.AccountId == actor && sessionIds.Contains(x.Id) && x.RevokedAt == null).ToArrayAsync(ct);
        foreach (var session in sessions) session.RevokedAt = clock.GetUtcNow();
        db.RemoveRange(logins); db.RemoveRange(tokens);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<Guid> ResolveAsync(long telegramId, CancellationToken ct)
    {
        var key = telegramId.ToString(CultureInfo.InvariantCulture);
        var login = await db.Set<IdentityUserLogin<Guid>>().AsNoTracking().SingleOrDefaultAsync(x => x.LoginProvider == Provider && x.ProviderKey == key, ct)
            ?? throw new MissingResourceException();
        await RequireStudent(login.UserId, ct);
        return login.UserId;
    }

    private async Task RequireStudent(Guid actor, CancellationToken ct)
    {
        if (!await db.Participants.AnyAsync(x => x.Id == actor, ct) ||
            !await permissions.HasAsync(actor, "profile.read", AccessScope.Own, actor, ct) ||
            !await permissions.HasAsync(actor, "lessons.receive", AccessScope.Own, actor, ct) ||
            !await permissions.HasAsync(actor, "exercises.submit", AccessScope.Own, actor, ct)) throw new AccessDeniedException();
    }

    private static string Hash(string code) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code)));
}
