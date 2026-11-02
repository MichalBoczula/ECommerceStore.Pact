using MongoDB.Bson.Serialization.Attributes;

namespace ECommerceStore.Pact.Infrastructure.Registry;

internal sealed class IntegrationDocument
{
    [BsonId]
    public required string Id { get; init; }
    public required string ConsumerApplicationId { get; init; }
    public required string ProviderApplicationId { get; init; }
    public DateTime RegisteredAtUtc { get; init; }
}
