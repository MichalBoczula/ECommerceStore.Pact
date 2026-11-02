using MongoDB.Bson.Serialization.Attributes;

namespace ECommerceStore.Pact.Infrastructure.Registry;

internal sealed class ApplicationVersionDocument
{
    [BsonId]
    public required string Id { get; init; }
    public required string ApplicationId { get; init; }
    public required string CommitSha { get; init; }
    public required string RepositoryUrl { get; init; }
    public string? ImageDigest { get; init; }
    public DateTime RegisteredAtUtc { get; init; }
}
