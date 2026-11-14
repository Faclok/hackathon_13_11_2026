namespace BashkirSchool.Domain;

public sealed class DomainException(string message) : Exception(message);

public static class Guard
{
    public static string Text(string? value, string name, int max = 10000)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > max)
            throw new DomainException($"{name} must contain 1..{max} characters.");
        return value;
    }
}
