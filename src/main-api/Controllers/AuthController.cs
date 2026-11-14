using Microsoft.AspNetCore.Mvc;

/// <summary>Handles account registration, login, and the current user's profile.</summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(AuthService auth) : ApiControllerBase
{
    /// <summary>Creates a password account and returns its bearer token.</summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] Credentials input) => Respond(await auth.Register(input));

    /// <summary>Authenticates an existing password account.</summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] Login input) => Respond(await auth.Login(input));

    /// <summary>Returns profile and point total for the bearer token.</summary>
    [HttpGet("/api/me")]
    public async Task<IActionResult> Me() => Respond(await auth.Me(Request.Headers.Authorization.ToString()));
}
