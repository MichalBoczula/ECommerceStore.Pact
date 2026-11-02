namespace ECommerceStore.Pact.Domain;

public sealed class Integration
{
    public Guid Id { get; }
    public IntegrationIdentity Identity { get; }
    public DateTimeOffset RegisteredAt { get; }

    private Integration(Guid id, IntegrationIdentity identity, DateTimeOffset registeredAt)
    {
        if (id == Guid.Empty)
        {
            throw new RegistryValidationException("id", "The integration ID cannot be empty.");
        }

        ArgumentNullException.ThrowIfNull(identity);
        Id = id;
        Identity = identity;
        RegisteredAt = RegistryIdentity.Timestamp(registeredAt);
    }

    public static Integration Register(IntegrationIdentity identity, DateTimeOffset registeredAt) =>
        new(Guid.NewGuid(), identity, registeredAt);

    public static Integration Rehydrate(Guid id, IntegrationIdentity identity, DateTimeOffset registeredAt) =>
        new(id, identity, registeredAt);
}
