namespace ECommerceStore.Pact.Domain;

public sealed record IntegrationIdentity
{
    public string ConsumerApplicationId { get; }
    public string ProviderApplicationId { get; }

    public IntegrationIdentity(string? consumerApplicationId, string? providerApplicationId)
    {
        ConsumerApplicationId = RegistryIdentity.ApplicationId(consumerApplicationId, "consumerApplicationId");
        ProviderApplicationId = RegistryIdentity.ApplicationId(providerApplicationId, "providerApplicationId");
        if (ConsumerApplicationId == ProviderApplicationId)
        {
            throw new RegistryValidationException("providerApplicationId", "Consumer and provider must be different applications.");
        }
    }
}
