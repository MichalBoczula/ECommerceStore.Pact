using ECommerceStore.Pact.Domain;

namespace ECommerceStore.Pact.Api.Registry;

public sealed record ApplicationVersionResultDto(Guid Id, string ApplicationId, string CommitSha,
    string RepositoryUrl, string? ImageDigest, DateTimeOffset RegisteredAt)
{
    internal static ApplicationVersionResultDto FromDomain(ApplicationVersion value) => new(value.Id,
        value.Identity.ApplicationId, value.Identity.CommitSha, value.RepositoryUrl, value.ImageDigest, value.RegisteredAt);
}
