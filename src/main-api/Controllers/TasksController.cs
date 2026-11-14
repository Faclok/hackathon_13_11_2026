using Microsoft.AspNetCore.Mvc;

/// <summary>Serves the daily translation task and records answers.</summary>
[ApiController]
[Route("api/tasks")]
public sealed class TasksController(AppDbContext db, TaskService tasks) : ApiControllerBase
{
    /// <summary>Returns today's task for the authenticated user.</summary>
    [HttpGet("today")]
    public async Task<IActionResult> Today()
    {
        var userId = await Sessions.User(Request, db);
        return userId is null ? Unauthorized() : Ok(await tasks.Today(userId.Value));
    }

    /// <summary>Submits or updates the user's answer to a task they own.</summary>
    [HttpPost("{id:guid}/submission")]
    public async Task<IActionResult> Submit(Guid id, [FromBody] Submission input)
    {
        var userId = await Sessions.User(Request, db);
        return userId is null ? Unauthorized() : Respond(await tasks.Submit(userId.Value, id, input));
    }
}
