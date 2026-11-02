namespace ECommerceStore.Pact.Domain.Contracts;

public sealed class ContractPublication
{
    public Guid Id { get; }
    public Guid BindingId { get; }
    public ContractProvenance Provenance { get; }
    public DateTimeOffset PublishedAt { get; }

    private ContractPublication(Guid id, Guid bindingId, ContractProvenance provenance, DateTimeOffset publishedAt)
    {
        if (id == Guid.Empty || bindingId == Guid.Empty)
            throw new RegistryValidationException("publication", "Publication and binding identities cannot be empty.");
        Id = id;
        BindingId = bindingId;
        Provenance = provenance;
        PublishedAt = RegistryIdentity.Timestamp(publishedAt);
    }

    public static ContractPublication Create(ContractBinding binding, ContractProvenance provenance, DateTimeOffset now) =>
        new(Guid.NewGuid(), binding.Id, provenance, now);
    public static ContractPublication Rehydrate(Guid id, Guid bindingId, ContractProvenance provenance, DateTimeOffset publishedAt) =>
        new(id, bindingId, provenance, publishedAt);
}
