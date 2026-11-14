using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BashkirSchool.Application;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BashkirSchool.Infrastructure;

/// <summary>Validates Telegram launch data and opens a session for an already linked participant.</summary>
public sealed class TelegramMiniAppAuth(CoreDbContext db, TelegramStudents students, Sessions sessions,
    IConfiguration configuration, TimeProvider clock)
{
    public async Task<AccessTokenResponse> SignInAsync(string initData, CancellationToken ct)
    {
        var telegramId = Validate(initData);
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var accountId = await students.ResolveAsync(telegramId, ct);
        var account = await db.Users.SingleAsync(x => x.Id == accountId && x.Active, ct);
        var session = await sessions.OpenAsync(account, ct, telegramId);
        await transaction.CommitAsync(ct);
        return session;
    }

    private long Validate(string initData)
    {
        var token = configuration["Telegram:BotToken"];
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(initData) || initData.Length > 16384)
            throw new AccessDeniedException();
        var fields = QueryHelpers.ParseQuery(initData);
        if (fields.Any(x => x.Value.Count != 1) || !fields.TryGetValue("hash", out var hash) || hash.ToString().Length != 64)
            throw new AccessDeniedException();
        var check = string.Join('\n', fields.Where(x => x.Key != "hash").OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => x.Key + "=" + x.Value.ToString()));
        var secret = HMACSHA256.HashData("WebAppData"u8, Encoding.UTF8.GetBytes(token));
        var expected = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(check));
        byte[] supplied;
        try { supplied = Convert.FromHexString(hash.ToString()); }
        catch (FormatException) { throw new AccessDeniedException(); }
        if (!CryptographicOperations.FixedTimeEquals(expected, supplied) ||
            !fields.TryGetValue("auth_date", out var date) ||
            !long.TryParse(date.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp))
            throw new AccessDeniedException();
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        if (timestamp < now - 300 || timestamp > now + 30) throw new AccessDeniedException();
        try
        {
            using var user = JsonDocument.Parse(fields["user"].ToString());
            if (user.RootElement.ValueKind == JsonValueKind.Object && user.RootElement.TryGetProperty("id", out var id) &&
                id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var value) && value > 0) return value;
        }
        catch (JsonException) { }
        throw new AccessDeniedException();
    }
}
