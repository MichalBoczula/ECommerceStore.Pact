using ECommerceStore.Pact.Domain;

namespace ECommerceStore.Pact.Api.Registry;

public sealed record IntegrationResultDto(Guid Id, string ConsumerApplicationId, string ProviderApplicationId,
    DateTimeOffset RegisteredAt)
{
    internal static IntegrationResultDto FromDomain(Integration value) => new(value.Id,
        value.Identity.ConsumerApplicationId, value.Identity.ProviderApplicationId, value.RegisteredAt);
}
