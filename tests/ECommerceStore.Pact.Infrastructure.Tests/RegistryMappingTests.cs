using ECommerceStore.Pact.Domain;
using ECommerceStore.Pact.Infrastructure.Registry;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace ECommerceStore.Pact.Infrastructure.Tests;

public sealed class RegistryMappingTests
{
    [Fact]
    public void ApplicationVersionSurvivesActualBsonRoundTrip()
    {
        var version = ApplicationVersion.Register(new ApplicationVersionIdentity("payments", new string('a', 40)),
            "https://github.com/MichalBoczula/ECommerceStorePayments", "sha256:" + new string('b', 64), DateTimeOffset.UtcNow);
        var bson = RegistryMapping.ToDocument(version).ToBson();
        var document = BsonSerializer.Deserialize<ApplicationVersionDocument>(bson);
        var readBack = RegistryMapping.ToDomain(document);
        Assert.Equal(version.Id, readBack.Id);
        Assert.Equal(version.RegisteredAt, readBack.RegisteredAt);
        Assert.True(version.HasSameMetadata(readBack));
    }

    [Fact]
    public void IntegrationSurvivesActualBsonRoundTrip()
    {
        var integration = Integration.Register(new IntegrationIdentity("payments", "invoice"), DateTimeOffset.UtcNow);
        var bson = RegistryMapping.ToDocument(integration).ToBson();
        var readBack = RegistryMapping.ToDomain(BsonSerializer.Deserialize<IntegrationDocument>(bson));
        Assert.Equal(integration.Id, readBack.Id);
        Assert.Equal(integration.Identity, readBack.Identity);
        Assert.Equal(integration.RegisteredAt, readBack.RegisteredAt);
    }

    [Fact]
    public void CorruptStorageIsNotReportedAsCallerValidationFailure()
    {
        var broken = new IntegrationDocument
        {
            Id = Guid.NewGuid().ToString(),
            ConsumerApplicationId = "payments",
            ProviderApplicationId = "payments",
            RegisteredAtUtc = DateTime.UtcNow
        };
        Assert.Throws<InvalidOperationException>(() => RegistryMapping.ToDomain(broken));
    }
}
