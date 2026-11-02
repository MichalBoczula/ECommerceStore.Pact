using ECommerceStore.Pact.Domain;
using ECommerceStore.Pact.Domain.Contracts;
using ECommerceStore.Pact.Domain.History;

namespace ECommerceStore.Pact.Infrastructure.History;

internal static class ComparisonMapping
{
    internal static ContractComparisonDocument ToDocument(ContractComparison value) => new()
    {
        Id = value.Id.ToString("D"),
        Key = value.Key,
        Profile = value.Profile,
        ConsumerApplicationId = value.Integration.ConsumerApplicationId,
        ProviderApplicationId = value.Integration.ProviderApplicationId,
        Before = Source(value.Before),
        After = Source(value.After),
        BaselineRef = value.BaselineRef,
        PublisherId = value.PublisherId,
        JsonChanged = value.JsonChanged,
        Tool = value.Schema.Tool,
        ToolVersion = value.Schema.Version,
        SchemaAssessment = value.Schema.Assessment.ToString(),
        Limitations = value.Schema.Limitations.ToList(),
        Findings = value.Schema.Findings.Select(f => new SchemaFindingDocument { Code = f.Code, Level = f.Level, Message = f.Message, Method = f.Method, Path = f.Path }).ToList(),
        ComparedAtUtc = value.ComparedAt.UtcDateTime
    };
    private static ComparisonSourceDocument Source(ComparisonSource value) => new()
    {
        PublicationId = value.PublicationId.ToString("D"),
        BindingId = value.BindingId.ToString("D"),
        SourceVersionId = value.SourceVersionId.ToString("D"),
        ApplicationId = value.Version.ApplicationId,
        CommitSha = value.Version.CommitSha,
        Kind = value.Kind.ToString(),
        ArtifactSha256 = value.ArtifactSha256,
        SourceRef = value.SourceRef
    };
    private static ComparisonSource Source(ComparisonSourceDocument value) => new(Guid.Parse(value.PublicationId), Guid.Parse(value.BindingId), Guid.Parse(value.SourceVersionId),
        new ApplicationVersionIdentity(value.ApplicationId, value.CommitSha), Enum.Parse<ContractKind>(value.Kind), value.ArtifactSha256, value.SourceRef);

    internal static ContractComparison ToDomain(ContractComparisonDocument value)
    {
        try
        {
            var schema = new SchemaAnalysis(value.Tool, value.ToolVersion, Enum.Parse<SchemaAssessment>(value.SchemaAssessment),
                value.Findings.Select(f => new SchemaFinding(f.Code, f.Level, f.Message, f.Method, f.Path)), value.Limitations);
            var result = ContractComparison.Rehydrate(Guid.Parse(value.Id), new IntegrationIdentity(value.ConsumerApplicationId, value.ProviderApplicationId),
                Source(value.Before), Source(value.After), value.BaselineRef, value.PublisherId, value.JsonChanged, schema, new DateTimeOffset(value.ComparedAtUtc, TimeSpan.Zero), value.Profile);
            if (result.Key != value.Key) throw new InvalidOperationException("Invalid stored comparison key.");
            return result;
        }
        catch (Exception exception) when (exception is RegistryValidationException or ArgumentException or FormatException)
        { throw new InvalidOperationException("A stored comparison is invalid.", exception); }
    }
}
