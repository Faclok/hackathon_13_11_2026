using Microsoft.EntityFrameworkCore;
using Npgsql;

/// <summary>Registers password accounts, creates sessions, and reads the current profile.</summary>
public sealed class AuthService(AppDbContext db)
{
    /// <summary>Validates registration and returns a new bearer token.</summary>
    public async Task<ServiceResult> Register(Credentials input)
    {
        var username = input.Username?.Trim().ToLowerInvariant() ?? "";
        if (username.Length is < 3 or > 32 || input.Password is null || input.Password.Length < 8 || string.IsNullOrWhiteSpace(input.DisplayName) || input.DisplayName.Trim().Length > 80)
            return ServiceResult.BadRequest(new { error = "Укажите логин (3–32), имя и пароль (не менее 8 символов)." });
        var user = new AppUser { Username = username, PasswordHash = Passwords.Hash(input.Password), DisplayName = input.DisplayName.Trim() };
        db.Users.Add(user);
        try { await db.SaveChangesAsync(); return ServiceResult.Success(await Sessions.Create(db, user)); }
        // The unique index handles simultaneous attempts to claim the same username.
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { return ServiceResult.Conflict(new { error = "Этот логин уже занят." }); }
    }

    /// <summary>Checks a password and issues a session for the account.</summary>
    public async Task<ServiceResult> Login(Login input)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == (input.Username ?? "").Trim().ToLower());
        if (user?.PasswordHash is not string hash || !Passwords.Verify(input.Password ?? "", hash)) return ServiceResult.Unauthorized();
        return ServiceResult.Success(await Sessions.Create(db, user));
    }

    /// <summary>Resolves a bearer token and includes points earned by the user.</summary>
    public async Task<ServiceResult> Me(string authorization)
    {
        var userId = await Sessions.User(authorization, db);
        if (userId is null) return ServiceResult.Unauthorized();
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
        var points = await db.PointEvents.Where(p => p.UserId == userId).SumAsync(p => (int?)p.Points) ?? 0;
        return ServiceResult.Success(new { id = user.Id, display_name = user.DisplayName, username = user.Username,
            telegram_id = user.TelegramId, lang_pair = user.LangPair, points });
    }
}
