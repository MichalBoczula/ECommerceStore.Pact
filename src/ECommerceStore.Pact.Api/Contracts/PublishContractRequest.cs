using System.Text.Json.Serialization;
using ECommerceStore.Pact.Application.Contracts;

namespace ECommerceStore.Pact.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PublishContractRequest
{
    public required string SourceApplicationId { get; init; }
    public required string CommitSha { get; init; }
    public required string Kind { get; init; }
    public string? ConsumerApplicationId { get; init; }
    public string? ProviderApplicationId { get; init; }
    public required string SourceRef { get; init; }
    public required string BuildUrl { get; init; }
    public required string ContentBase64 { get; init; }

    internal ContractPublicationCommand ToCommand() => new(SourceApplicationId, CommitSha, Kind,
        ConsumerApplicationId, ProviderApplicationId, SourceRef, BuildUrl, ContentBase64);
}
