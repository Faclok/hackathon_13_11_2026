using Npgsql;

namespace BashkirSchool.Infrastructure;

public static class DatabaseConflicts
{
    public static bool IsConflict(Exception exception) => FindState(exception) is "40001" or "40P01" or "23505";
    public static bool IsRetryable(Exception exception) => FindState(exception) is "40001" or "40P01";
    private static string? FindState(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException postgres) return postgres.SqlState;
        return null;
    }
}
