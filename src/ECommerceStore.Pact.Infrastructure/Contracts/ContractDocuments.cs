using MongoDB.Bson.Serialization.Attributes;

namespace ECommerceStore.Pact.Infrastructure.Contracts;

internal sealed class ContractArtifactDocument
{
    [BsonId] public required string Id { get; init; }
    public required string Kind { get; init; }
    public required string SpecificationVersion { get; init; }
    public required byte[] Content { get; init; }
    public DateTime StoredAtUtc { get; init; }
}

internal sealed class ContractBindingDocument
{
    [BsonId] public required string Id { get; init; }
    public required string SourceVersionId { get; init; }
    public required string SourceApplicationId { get; init; }
    public required string CommitSha { get; init; }
    public required string Kind { get; init; }
    public required string IntegrationKey { get; init; }
    public string? ConsumerApplicationId { get; init; }
    public string? ProviderApplicationId { get; init; }
    public required string ArtifactSha256 { get; init; }
    public DateTime BoundAtUtc { get; init; }
}

internal sealed class ContractPublicationDocument
{
    [BsonId] public required string Id { get; init; }
    public required string BindingId { get; init; }
    public required string SourceRef { get; init; }
    public required string BuildUrl { get; init; }
    public required string PublisherId { get; init; }
    public required string RepositoryUrl { get; init; }
    public required string ProvenanceHash { get; init; }
    public DateTime PublishedAtUtc { get; init; }
}
