using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

/// <summary>Hashes passwords with a random salt and PBKDF2-SHA256.</summary>
static class Passwords
{
    /// <summary>Stores salt and hash in a version-local colon-separated value.</summary>
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210_000, HashAlgorithmName.SHA256, 32);
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    /// <summary>Checks the stored hash without timing-sensitive byte comparison.</summary>
    public static bool Verify(string password, string stored)
    {
        try
        {
            var parts = stored.Split(':');
            if (parts.Length != 2) return false;
            var salt = Convert.FromBase64String(parts[0]);
            var expected = Convert.FromBase64String(parts[1]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210_000, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }
}

/// <summary>Issues opaque bearer tokens while storing only their SHA-256 hashes.</summary>
static class Sessions
{
    /// <summary>Creates a 30-day session and returns the raw token only to the caller.</summary>
    public static async Task<object> Create(AppDbContext db, AppUser user)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        db.Sessions.Add(new UserSession { TokenHash = Sha(token), UserId = user.Id, ExpiresAt = DateTime.UtcNow.AddDays(30) });
        await db.SaveChangesAsync();
        return new { token, user = new { id = user.Id, display_name = user.DisplayName, lang_pair = user.LangPair } };
    }

    /// <summary>Reads the bearer token from an HTTP request.</summary>
    public static Task<Guid?> User(HttpRequest request, AppDbContext db) => User(request.Headers.Authorization.ToString(), db);

    /// <summary>Returns the user for a valid, unexpired token.</summary>
    public static async Task<Guid?> User(string authorization, AppDbContext db)
    {
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        var token = authorization[7..].Trim();
        if (token.Length != 64) return null;
        return await db.Sessions.AsNoTracking().Where(s => s.TokenHash == Sha(token) && s.ExpiresAt > DateTime.UtcNow)
            .Select(s => (Guid?)s.UserId).FirstOrDefaultAsync();
    }

    private static string Sha(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

/// <summary>Checks the shared secret used by internal service calls.</summary>
static class Internal
{
    /// <summary>Uses constant-time comparison after checking header length.</summary>
    public static bool Valid(HttpRequest request, string expected)
    {
        var actual = request.Headers["X-Internal-Key"].ToString();
        return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(expected));
    }
}

