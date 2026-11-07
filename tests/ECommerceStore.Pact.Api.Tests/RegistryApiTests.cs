using System.Net;
using System.Text.Json;
using ECommerceStore.Pact.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ECommerceStore.Pact.Api.Tests;

public sealed class RegistryApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Mongo:ConnectionString"] = "mongodb://127.0.0.1:1",
                ["Mongo:DatabaseName"] = "pact-api-tests",
                ["Mongo:TimeoutSeconds"] = "1"
            }.Concat(PublisherTestSettings.Configuration)));
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        PublisherTestSettings.Authorize(client);
    }
}

public sealed class RegistryApiTests(RegistryApiFactory factory) : IClassFixture<RegistryApiFactory>
{
    [Fact]
    public async Task LivenessDoesNotRequireMongoDb()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", body.RootElement.GetProperty("status").GetString());
        Assert.Empty(body.RootElement.GetProperty("checks").EnumerateObject());
    }

    [Fact]
    public async Task ReadinessFailsWithoutMongoDbAndDoesNotExposeConnectionDetails()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(content);
        Assert.Equal("Unhealthy", body.RootElement.GetProperty("checks").GetProperty("mongodb").GetString());
        Assert.DoesNotContain("127.0.0.1", content);
        Assert.DoesNotContain("Exception", content);
    }

    [Fact]
    public async Task OpenApiIsGeneratedByTheApplication()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.StartsWith("3.", body.RootElement.GetProperty("openapi").GetString());
        Assert.Equal("ECommerceStore Pact Registry", body.RootElement.GetProperty("info").GetProperty("title").GetString());
    }

    [Fact]
    public async Task SwaggerUiUsesTheExistingOpenApiDocumentInProduction()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;

        using var response = await client.GetAsync("/swagger");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("swagger-ui-bundle.js", await response.Content.ReadAsStringAsync());

        var configuration = await client.GetStringAsync("/swagger/index.js");
        Assert.Contains("../openapi/v1.json", configuration);
    }

    [Fact]
    public async Task UnknownRouteReturnsProblemDetailsWithTraceId()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/not-a-route");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(404, body.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
    }
}
