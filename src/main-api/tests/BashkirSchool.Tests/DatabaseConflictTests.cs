using BashkirSchool.Infrastructure;
using Npgsql;

namespace BashkirSchool.Tests;

public sealed class DatabaseConflictTests
{
    [Fact]
    public void WrappedSerializationFailureIsAConflictButDuplicateKeyIsNotAutomaticallyRetried()
    {
        var wrapped = new InvalidOperationException("Transient provider failure", new PostgresException("Serialization", "ERROR", "ERROR", "40001"));
        Assert.True(DatabaseConflicts.IsConflict(wrapped)); Assert.True(DatabaseConflicts.IsRetryable(wrapped));
        var duplicate = new PostgresException("Duplicate key", "ERROR", "ERROR", "23505");
        Assert.True(DatabaseConflicts.IsConflict(duplicate)); Assert.False(DatabaseConflicts.IsRetryable(duplicate));
        Assert.False(DatabaseConflicts.IsConflict(new InvalidOperationException("Unrelated failure")));
    }
}
