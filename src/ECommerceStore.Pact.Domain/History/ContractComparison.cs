using ECommerceStore.Pact.Domain.Contracts;
using ECommerceStore.Pact.Domain.Verification;

namespace ECommerceStore.Pact.Domain.History;

public enum SchemaAssessment { NoBreakingChangesDetected, PotentiallyBreaking, NotAssessed }

public sealed record SchemaFinding(string Code, int Level, string Message, string? Method, string? Path);

public sealed class SchemaAnalysis
{
    public string Tool { get; }
    public string Version { get; }
    public SchemaAssessment Assessment { get; }
    public IReadOnlyList<SchemaFinding> Findings { get; }
    public IReadOnlyList<string> Limitations { get; }
    public SchemaAnalysis(string tool, string version, SchemaAssessment assessment, IEnumerable<SchemaFinding> findings, IEnumerable<string> limitations)
    {
        Tool = VerificationValues.Text(tool, "tool", 100); Version = VerificationValues.Text(version, "toolVersion", 100);
        var items = findings.Take(1001).ToArray(); var notes = limitations.Take(11).ToArray();
        if (!Enum.IsDefined(assessment) || items.Length > 1000 || notes.Length > 10 ||
            items.Any(f => f.Level is < 1 or > 3 || string.IsNullOrEmpty(f.Code) || f.Code.Length > 200 || string.IsNullOrEmpty(f.Message) || f.Message.Length > 8192) ||
            notes.Any(note => string.IsNullOrWhiteSpace(note) || note.Length > 2048) ||
            (assessment == SchemaAssessment.NoBreakingChangesDetected && (notes.Length != 0 || items.Any(f => f.Level >= 2))))
            throw new RegistryValidationException("schemaAnalysis", "Invalid or unbounded schema analysis.");
        Assessment = assessment; Findings = Array.AsReadOnly(items); Limitations = Array.AsReadOnly(notes);
    }
}

public sealed record ComparisonSource(Guid PublicationId, Guid BindingId, Guid SourceVersionId,
    ApplicationVersionIdentity Version, ContractKind Kind, string ArtifactSha256, string SourceRef);

public sealed class ContractComparison
{
    public const string CurrentProfile = "registry-comparison-v1/oasdiff-1.35.0";
    public string Profile { get; }
    public Guid Id { get; }
    public string Key { get; }
    public IntegrationIdentity Integration { get; }
    public ComparisonSource Before { get; }
    public ComparisonSource After { get; }
    public string BaselineRef { get; }
    public string PublisherId { get; }
    public bool ContentChanged { get; }
    public bool JsonChanged { get; }
    public SchemaAnalysis Schema { get; }
    public DateTimeOffset ComparedAt { get; }

    public static string Identity(IntegrationIdentity integration, Guid before, Guid after, string baselineRef, string? profile = null) =>
        VerificationValues.Hash("comparison-key-v1", profile ?? CurrentProfile, integration.ConsumerApplicationId, integration.ProviderApplicationId, before, after, baselineRef);

    private ContractComparison(Guid id, IntegrationIdentity integration, ComparisonSource before, ComparisonSource after,
        string baselineRef, string publisherId, bool jsonChanged, SchemaAnalysis schema, DateTimeOffset comparedAt, string profile)
    {
        if (id == Guid.Empty || before.Kind != after.Kind || before.Version.ApplicationId != after.Version.ApplicationId || before.SourceRef != baselineRef)
            throw new RegistryValidationException("comparison", "Select publications of the same kind/application and an explicit matching baseline ref.");
        var owner = before.Kind == ContractKind.ConsumerPact ? integration.ConsumerApplicationId : integration.ProviderApplicationId;
        foreach (var source in new[] { before, after })
            if (!Enum.IsDefined(source.Kind) || source.Version.ApplicationId != owner || source.PublicationId == Guid.Empty ||
                source.BindingId == Guid.Empty || source.SourceVersionId == Guid.Empty || ContractArtifact.NormalizeHash(source.ArtifactSha256) != source.ArtifactSha256)
                throw new RegistryValidationException("comparison", "Invalid comparison source references.");
        Profile = VerificationValues.Text(profile, "profile", 150);
        Id = id; Integration = integration; Before = before; After = after; BaselineRef = baselineRef;
        PublisherId = RegistryIdentity.ApplicationId(publisherId, "publisherId");
        ContentChanged = before.ArtifactSha256 != after.ArtifactSha256;
        if (!ContentChanged && jsonChanged) throw new RegistryValidationException("jsonChanged", "Identical bytes cannot have different JSON content.");
        JsonChanged = jsonChanged; Schema = schema; ComparedAt = RegistryIdentity.Timestamp(comparedAt);
        Key = Identity(integration, before.PublicationId, after.PublicationId, baselineRef, Profile);
    }

    public static ContractComparison Create(IntegrationIdentity integration, ComparisonSource before, ComparisonSource after, string baselineRef,
        string publisherId, bool jsonChanged, SchemaAnalysis schema, DateTimeOffset now) => new(Guid.NewGuid(), integration, before, after, baselineRef, publisherId, jsonChanged, schema, now, CurrentProfile);
    public static ContractComparison Rehydrate(Guid id, IntegrationIdentity integration, ComparisonSource before, ComparisonSource after, string baselineRef,
        string publisherId, bool jsonChanged, SchemaAnalysis schema, DateTimeOffset now, string profile) => new(id, integration, before, after, baselineRef, publisherId, jsonChanged, schema, now, profile);
}
