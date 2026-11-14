using System.Security.Claims;
using BashkirSchool.Application;

namespace BashkirSchool.Host;

public static class ClassInvitationEndpoints
{
    public static void MapClassInvitationEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();
        api.MapPost("/classes/{classId:guid}/invitations", async (ClaimsPrincipal user, Guid classId,
            InviteStudentRequest request, IClassInvitations invitations, CancellationToken ct) =>
        {
            var invitation = await invitations.InviteAsync(Subject(user), classId, request, ct);
            return TypedResults.Created($"/api/classes/{classId}/invitations", invitation);
        }).WithName("InviteStudentToClass").WithSummary("Invite a self-registered participant to the teacher's own class.")
            .ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        api.MapGet("/classes/{classId:guid}/invitations", (ClaimsPrincipal user, Guid classId, int? offset, int? limit,
            IClassInvitations invitations, CancellationToken ct) => invitations.ForClassAsync(Subject(user), classId, offset ?? 0, limit ?? 50, ct))
            .WithName("ListClassInvitations").WithSummary("Read invitations and decisions for the teacher's own class.");
        api.MapGet("/me/class-invitations", (ClaimsPrincipal user, int? offset, int? limit,
            IClassInvitations invitations, CancellationToken ct) => invitations.ForStudentAsync(Subject(user), offset ?? 0, limit ?? 50, ct))
            .WithName("ListMyClassInvitations").WithSummary("Read the current participant's class invitations.");
        api.MapPost("/me/class-invitations/{invitationId:guid}/response", (ClaimsPrincipal user, Guid invitationId,
            RespondToClassInvitationRequest request, IClassInvitations invitations, CancellationToken ct) =>
            invitations.RespondAsync(Subject(user), invitationId, request, ct))
            .WithName("RespondToClassInvitation").WithSummary("Accept or decline an invitation addressed to the current participant.")
            .ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
    }

    private static Guid Subject(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue("sub")!);
}
