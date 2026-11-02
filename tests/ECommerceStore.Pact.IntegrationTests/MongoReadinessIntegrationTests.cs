using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.MongoDb;

namespace ECommerceStore.Pact.IntegrationTests;

public sealed class MongoFixture : IAsyncLifetime
{
    private MongoDbContainer? _container;
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable("PACT_TEST_MONGODB");
        if (!string.IsNullOrWhiteSpace(external))
        {
            ConnectionString = external;
            return;
        }

        _container = new MongoDbBuilder("mongo:8.0").Build();
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}

public sealed class MongoReadinessIntegrationTests(MongoFixture mongo) : IClassFixture<MongoFixture>
{
    [Fact]
    public async Task ReadinessChecksActualMongoDb()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Mongo:ConnectionString"] = mongo.ConnectionString,
                    ["Mongo:DatabaseName"] = $"pact-tests-{Guid.NewGuid():N}"
                }));
        });
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", body.RootElement.GetProperty("checks").GetProperty("mongodb").GetString());
    }
}
