using Microsoft.AspNetCore.Mvc;

/// <summary>Exposes the public participant leaderboard.</summary>
[ApiController]
public sealed class CommunityController(CommunityService community) : ControllerBase
{
    /// <summary>Returns the top participants ordered by points and answer count.</summary>
    [HttpGet("api/leaderboard")]
    public async Task<IActionResult> Leaderboard() => Ok(await community.Leaderboard());
}
