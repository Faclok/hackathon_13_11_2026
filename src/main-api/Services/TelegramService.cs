using Microsoft.EntityFrameworkCore;

/// <summary>Connects Telegram identities to the main user and task workflow.</summary>
public sealed class TelegramService(AppDbContext db, TaskService tasks)
{
    /// <summary>Creates a Telegram user or refreshes the display name from /start.</summary>
    public async Task<object> Start(long telegramId, TelegramStart input)
    {
        var name = string.IsNullOrWhiteSpace(input.Name) ? "Участник" : input.Name.Trim()[..Math.Min(input.Name.Trim().Length, 80)];
        var user = await db.Users.FirstOrDefaultAsync(u => u.TelegramId == telegramId);
        if (user is null) { user = new AppUser { TelegramId = telegramId, DisplayName = name }; db.Users.Add(user); }
        else user.DisplayName = name;
        await db.SaveChangesAsync();
        return new { id = user.Id, display_name = user.DisplayName };
    }

    /// <summary>Returns today's task for an existing Telegram user.</summary>
    public async Task<ServiceResult> Task(long telegramId)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.TelegramId == telegramId);
        if (user is null) return ServiceResult.NotFound();
        var task = await tasks.Today(user.Id);
        return task is null ? ServiceResult.NotFound() : ServiceResult.Success(task);
    }

    /// <summary>Submits a Telegram answer through the shared task service.</summary>
    public async Task<ServiceResult> Submit(long telegramId, Submission input)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.TelegramId == telegramId);
        if (user is null || input.TaskId is not Guid taskId)
            return ServiceResult.BadRequest(new { error = "Сначала запросите задание командой /task." });
        return await tasks.Submit(user.Id, taskId, input);
    }

    /// <summary>Returns the user's score accumulated from point events.</summary>
    public async Task<ServiceResult> Me(long telegramId)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.TelegramId == telegramId);
        if (user is null) return ServiceResult.NotFound();
        var points = await db.PointEvents.Where(p => p.UserId == user.Id).SumAsync(p => (int?)p.Points) ?? 0;
        return ServiceResult.Success(new { display_name = user.DisplayName, points });
    }
}
