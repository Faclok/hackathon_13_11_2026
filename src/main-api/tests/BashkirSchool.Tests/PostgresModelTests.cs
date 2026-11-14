using BashkirSchool.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BashkirSchool.Tests;

public sealed class PostgresModelTests
{
    [Fact]
    public void PostgresSchemaPreservesFractionalTrustAndPolicyShares()
    {
        using var db = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only").Options);
        var sql = db.Database.GenerateCreateScript();
        Assert.Contains("\"TrustAtSubmission\" numeric(18,12)", sql);
        Assert.Contains("\"SupportThreshold\" numeric(18,12)", sql);
        Assert.Contains("\"MaximumClassShare\" numeric(18,12)", sql);
        Assert.Contains("CREATE UNIQUE INDEX \"IX_Responses_AssignmentId\"", sql);
        Assert.Contains("CREATE UNIQUE INDEX \"IX_Deliveries_ProducerId_Key\"", sql);
    }
}
