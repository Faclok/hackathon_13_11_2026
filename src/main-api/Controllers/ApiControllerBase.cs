using Microsoft.AspNetCore.Mvc;

/// <summary>Maps service outcomes to the API's HTTP status and response body.</summary>
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>Preserves the same response contract across public and internal controllers.</summary>
    protected IActionResult Respond(ServiceResult result) => result.Status switch
    {
        ServiceStatus.Ok => result.Value is null ? Ok() : Ok(result.Value),
        ServiceStatus.BadRequest => BadRequest(result.Value),
        ServiceStatus.NotFound => NotFound(),
        ServiceStatus.Unauthorized => Unauthorized(),
        ServiceStatus.Conflict => Conflict(result.Value),
        _ => StatusCode(500)
    };
}
