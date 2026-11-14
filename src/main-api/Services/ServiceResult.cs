/// <summary>Outcomes understood by the controller response adapter.</summary>
public enum ServiceStatus { Ok, BadRequest, NotFound, Unauthorized, Conflict }

/// <summary>Business operation result independent of MVC result types.</summary>
public sealed record ServiceResult(ServiceStatus Status, object? Value = null)
{
    public static ServiceResult Success(object? value = null) => new(ServiceStatus.Ok, value);
    public static ServiceResult BadRequest(object value) => new(ServiceStatus.BadRequest, value);
    public static ServiceResult NotFound() => new(ServiceStatus.NotFound);
    public static ServiceResult Unauthorized() => new(ServiceStatus.Unauthorized);
    public static ServiceResult Conflict(object value) => new(ServiceStatus.Conflict, value);
}
