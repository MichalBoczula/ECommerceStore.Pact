using System.Text.Json.Serialization;

namespace ECommerceStore.Pact.Api.Registry;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegisterApplicationVersionRequest
{
    public required string CommitSha { get; init; }
    public required string RepositoryUrl { get; init; }
    public string? ImageDigest { get; init; }
}
