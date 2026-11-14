using Microsoft.AspNetCore.Mvc;

/// <summary>Routes used only by trusted services inside the Compose network.</summary>
[ApiController]
[Route("api/internal")]
public sealed class InternalController(AppDbContext db, TelegramService telegram, IConfiguration configuration) : ApiControllerBase
{
    // Defense in depth: the proxy also blocks this path from external traffic.
    private bool Valid() => Internal.Valid(Request, configuration["INTERNAL_API_KEY"]!);

    /// <summary>Resolves a bearer token to a user ID for another service.</summary>
    [HttpGet("resolve-token")]
    public async Task<IActionResult> ResolveToken()
    {
        if (!Valid()) return Unauthorized();
        var userId = await Sessions.User(Request, db);
        return userId is null ? Unauthorized() : Ok(new { userId });
    }

    /// <summary>Creates or updates the Telegram user's display name.</summary>
    [HttpPost("telegram/{telegramId:long}/start")]
    public async Task<IActionResult> Start(long telegramId, [FromBody] TelegramStart input) =>
        Valid() ? Ok(await telegram.Start(telegramId, input)) : Unauthorized();

    /// <summary>Returns the Telegram user's daily task.</summary>
    [HttpGet("telegram/{telegramId:long}/task")]
    public async Task<IActionResult> Task(long telegramId) =>
        Valid() ? Respond(await telegram.Task(telegramId)) : Unauthorized();

    /// <summary>Submits an answer from the Telegram bot.</summary>
    [HttpPost("telegram/{telegramId:long}/submit")]
    public async Task<IActionResult> Submit(long telegramId, [FromBody] Submission input) =>
        Valid() ? Respond(await telegram.Submit(telegramId, input)) : Unauthorized();

    /// <summary>Clears the bot's pending interaction; state is kept by the bot service.</summary>
    [HttpPost("telegram/{telegramId:long}/cancel")]
    public IActionResult Cancel(long telegramId) => Valid() ? Ok() : Unauthorized();

    /// <summary>Returns the Telegram user's name and score.</summary>
    [HttpGet("telegram/{telegramId:long}/me")]
    public async Task<IActionResult> Me(long telegramId) =>
        Valid() ? Respond(await telegram.Me(telegramId)) : Unauthorized();
}
