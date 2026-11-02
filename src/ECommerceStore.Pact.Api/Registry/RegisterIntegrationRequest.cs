using System.Text.Json.Serialization;

namespace ECommerceStore.Pact.Api.Registry;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegisterIntegrationRequest
{
    public required string ConsumerApplicationId { get; init; }
    public required string ProviderApplicationId { get; init; }
}
