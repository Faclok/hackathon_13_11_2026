namespace BashkirSchool.Domain;

public sealed class Fragment
{
    private Fragment() { }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ProducerId { get; private set; }
    public string ExternalId { get; private set; } = "";
    public string Text { get; private set; } = "";
    public string LanguagePair { get; private set; } = "";
    public string SourceJson { get; private set; } = "";

    public static Fragment Create(Guid producerId, string externalId, string text, string languagePair, string sourceJson)
    {
        if (producerId == Guid.Empty) throw new DomainException("Producer is required.");
        var languages = languagePair?.Split('-') ?? [];
        if (languages.Length != 2 || languages.Any(x => x.Length is < 2 or > 3 || !x.All(char.IsAsciiLetterLower)) || languages[0] == languages[1])
            throw new DomainException("Use a directed language pair such as ba-ru.");
        return new() { ProducerId = producerId, ExternalId = Guard.Text(externalId, nameof(externalId), 200),
            Text = Guard.Text(text, nameof(text)), LanguagePair = languagePair!, SourceJson = Guard.Text(sourceJson, nameof(sourceJson)) };
    }
}

public sealed class ContentDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProducerId { get; set; }
    public string Key { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public Guid CaseId { get; set; }
}

public sealed class TranslationCase
{
    private TranslationCase() { }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid FragmentId { get; private set; }
    public Guid ShownVariantId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public bool ExpertClosed { get; private set; }
    public long Revision { get; private set; }
    public Guid? ResolutionId { get; private set; }
    public bool NeedsExpert { get; private set; }
    public void Apply(ResolutionRevision resolution)
    {
        if (ExpertClosed || resolution.CaseId != Id || resolution.Revision != Revision + 1)
            throw new DomainException("Resolution is stale or the case is expert-closed.");
        Revision = resolution.Revision;
        ResolutionId = resolution.Id;
        ExpertClosed = resolution.Kind == ResolutionKind.Expert;
        NeedsExpert = false;
    }
    public void SetDisputed(bool disputed)
    {
        if (!ExpertClosed) NeedsExpert = disputed;
    }
    public static TranslationCase Open(Guid fragmentId, Guid machineVariantId, DateTimeOffset now) =>
        new() { FragmentId = fragmentId, ShownVariantId = machineVariantId, CreatedAt = now };
}

public sealed class TranslationVariant
{
    private TranslationVariant() { }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CaseId { get; private set; }
    public string Text { get; private set; } = "";
    public bool MachineGenerated { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public static TranslationVariant Create(Guid caseId, string text, bool machineGenerated, Guid? id = null,
        Guid? createdBy = null, DateTimeOffset? createdAt = null) =>
        new() { Id = id ?? Guid.NewGuid(), CaseId = caseId, Text = Guard.Text(text, nameof(text)), MachineGenerated = machineGenerated,
            CreatedBy = createdBy, CreatedAt = createdAt ?? DateTimeOffset.MinValue };
}
