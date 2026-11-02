using MongoDB.Driver;

namespace ECommerceStore.Pact.Infrastructure.Registry;

internal sealed class RegistryCollections(IMongoDatabase database)
{
    internal IMongoCollection<ApplicationVersionDocument> Versions { get; } =
        database.GetCollection<ApplicationVersionDocument>("application_versions");
    internal IMongoCollection<IntegrationDocument> Integrations { get; } =
        database.GetCollection<IntegrationDocument>("integrations");
}
