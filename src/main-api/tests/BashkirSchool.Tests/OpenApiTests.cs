using System.Text.Json;
using static BashkirSchool.Tests.CoreFactory;

namespace BashkirSchool.Tests;

public sealed class OpenApiTests
{
    [Fact]
    public async Task OpenApiDocumentsBearerSecurityAndActualIntakeAndAssignmentResponses()
    {
        using var factory = new CoreFactory(); using var admin = await factory.Bootstrap();
        using var document = JsonDocument.Parse(await admin.GetStringAsync("/openapi/v1.json"));
        var root = document.RootElement;
        Assert.Equal("bearer", root.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer").GetProperty("scheme").GetString());
        var delivery = root.GetProperty("paths").GetProperty("/api/deliveries").GetProperty("post");
        Assert.Equal("#/components/schemas/DeliveryResponse", delivery.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        Assert.Equal(1, delivery.GetProperty("security").GetArrayLength());
        var schemas = root.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.GetProperty("DeliverContentRequest").GetProperty("properties").TryGetProperty("fragment_id", out _));
        Assert.False(schemas.GetProperty("SubmitReviewRequest").GetProperty("properties").TryGetProperty("trustAtSubmission", out _));
        var next = root.GetProperty("paths").GetProperty("/api/reviews/next").GetProperty("post").GetProperty("responses");
        Assert.True(next.TryGetProperty("204", out _));
        Assert.True(next.TryGetProperty("200", out _));
    }
}
