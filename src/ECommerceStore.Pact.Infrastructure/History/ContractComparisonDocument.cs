using MongoDB.Bson.Serialization.Attributes;

namespace ECommerceStore.Pact.Infrastructure.History;

internal sealed class ContractComparisonDocument
{
    [BsonId] public required string Id { get; init; }
    public required string Key { get; init; }
    public required string Profile { get; init; }
    public required string ConsumerApplicationId { get; init; }
    public required string ProviderApplicationId { get; init; }
    public required ComparisonSourceDocument Before { get; init; }
    public required ComparisonSourceDocument After { get; init; }
    public required string BaselineRef { get; init; }
    public required string PublisherId { get; init; }
    public bool JsonChanged { get; init; }
    public required string Tool { get; init; }
    public required string ToolVersion { get; init; }
    public required string SchemaAssessment { get; init; }
    public required List<SchemaFindingDocument> Findings { get; init; }
    public required List<string> Limitations { get; init; }
    public DateTime ComparedAtUtc { get; init; }
}
internal sealed class ComparisonSourceDocument
{
    public required string PublicationId { get; init; }
    public required string BindingId { get; init; }
    public required string SourceVersionId { get; init; }
    public required string ApplicationId { get; init; }
    public required string CommitSha { get; init; }
    public required string Kind { get; init; }
    public required string ArtifactSha256 { get; init; }
    public required string SourceRef { get; init; }
}
internal sealed class SchemaFindingDocument
{
    public required string Code { get; init; }
    public int Level { get; init; }
    public required string Message { get; init; }
    public string? Method { get; init; }
    public string? Path { get; init; }
}
