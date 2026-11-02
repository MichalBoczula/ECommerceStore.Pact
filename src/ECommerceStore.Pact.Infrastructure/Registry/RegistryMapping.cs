using ECommerceStore.Pact.Domain;

namespace ECommerceStore.Pact.Infrastructure.Registry;

internal static class RegistryMapping
{
    internal static ApplicationVersionDocument ToDocument(ApplicationVersion value) => new()
    {
        Id = value.Id.ToString("D"),
        ApplicationId = value.Identity.ApplicationId,
        CommitSha = value.Identity.CommitSha,
        RepositoryUrl = value.RepositoryUrl,
        ImageDigest = value.ImageDigest,
        RegisteredAtUtc = value.RegisteredAt.UtcDateTime
    };

    internal static ApplicationVersion ToDomain(ApplicationVersionDocument value) => Rehydrate(() =>
        ApplicationVersion.Rehydrate(Guid.Parse(value.Id), new ApplicationVersionIdentity(value.ApplicationId, value.CommitSha),
            value.RepositoryUrl, value.ImageDigest, new DateTimeOffset(value.RegisteredAtUtc, TimeSpan.Zero)));

    internal static IntegrationDocument ToDocument(Integration value) => new()
    {
        Id = value.Id.ToString("D"),
        ConsumerApplicationId = value.Identity.ConsumerApplicationId,
        ProviderApplicationId = value.Identity.ProviderApplicationId,
        RegisteredAtUtc = value.RegisteredAt.UtcDateTime
    };

    internal static Integration ToDomain(IntegrationDocument value) => Rehydrate(() =>
        Integration.Rehydrate(Guid.Parse(value.Id), new IntegrationIdentity(value.ConsumerApplicationId, value.ProviderApplicationId),
            new DateTimeOffset(value.RegisteredAtUtc, TimeSpan.Zero)));

    private static T Rehydrate<T>(Func<T> factory)
    {
        try
        {
            return factory();
        }
        catch (Exception exception) when (exception is RegistryValidationException or ArgumentException or FormatException)
        {
            // Corrupt persisted data is a server error, not a caller validation failure.
            throw new InvalidOperationException("A stored registry record is invalid.", exception);
        }
    }
}
