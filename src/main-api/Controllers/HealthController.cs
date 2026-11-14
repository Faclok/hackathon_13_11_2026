using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

/// <summary>Checks whether the main API can connect to its database.</summary>
[ApiController]
public sealed class HealthController(AppDbContext db) : ControllerBase
{
    /// <summary>Returns 200 when PostgreSQL is available, otherwise 503.</summary>
    [HttpGet("api/health")]
    public async Task<IActionResult> Get() => await db.Database.CanConnectAsync()
        ? Ok(new { status = "ok" }) : StatusCode(503);
}
