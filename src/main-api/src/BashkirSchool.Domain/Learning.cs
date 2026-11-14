namespace BashkirSchool.Domain;

public sealed class VocabularyEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string LanguagePair { get; set; } = "";
    public string Word { get; set; } = "";
    public string Translation { get; set; } = "";
}

// An asset becomes visible to exercises only after synthesis and upload succeed.
public sealed class SentenceAudio
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FragmentId { get; set; }
    public string ObjectKey { get; set; } = "";
    public string ContentType { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
