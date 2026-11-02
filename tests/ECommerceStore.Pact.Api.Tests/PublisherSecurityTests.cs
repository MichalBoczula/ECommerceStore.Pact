using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ECommerceStore.Pact.Testing;
using ECommerceStore.Pact.Api.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace ECommerceStore.Pact.Api.Tests;

public sealed class PublisherSecurityTests(RegistryApiFactory factory) : IClassFixture<RegistryApiFactory>
{
    [Theory]
    [InlineData("/api/applications/payments/versions")]
    [InlineData("/api/integrations")]
    [InlineData("/api/contract-publications")]
    [InlineData("/api/verification-runs")]
    [InlineData("/api/contract-comparisons")]
    public async Task EveryWriteRequiresAuthenticationBeforeReadingTheBody(string path)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;
        using var response = await client.PostAsJsonAsync(path, new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UnknownTokenIsRejectedWithoutEchoingIt()
    {
        using var client = factory.CreateClient();
        PublisherTestSettings.Authorize(client, new string('x', 40));
        using var response = await client.PostAsJsonAsync("/api/integrations", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain(new string('x', 40), await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("version")]
    [InlineData("integration")]
    [InlineData("publication")]
    public async Task ScopedPublisherCannotWriteForAnotherApplication(string operation)
    {
        using var client = factory.CreateClient();
        PublisherTestSettings.Authorize(client, PublisherTestSettings.PaymentsToken);
        using var response = operation switch
        {
            "version" => await client.PostAsJsonAsync("/api/applications/invoice/versions", new { commitSha = new string('a', 40), repositoryUrl = "https://github.com/owner/repo" }),
            "integration" => await client.PostAsJsonAsync("/api/integrations", new { consumerApplicationId = "invoice", providerApplicationId = "products" }),
            _ => await client.PostAsJsonAsync("/api/contract-publications", new { sourceApplicationId = "invoice", commitSha = new string('a', 40), kind = "ProviderOpenApi", sourceRef = "refs/heads/main", buildUrl = "https://github.com/owner/repo/actions/runs/1", contentBase64 = "e30=" })
        };
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task OpenApiDeclaresSecurityForWritesOnly()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;
        using var response = await client.GetAsync("/openapi/v1.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var path = document.RootElement.GetProperty("paths").GetProperty("/api/contract-publications");
        Assert.True(path.GetProperty("post").TryGetProperty("security", out _));
        var read = document.RootElement.GetProperty("paths").GetProperty("/api/artifacts/{sha256}").GetProperty("get");
        Assert.False(read.TryGetProperty("security", out _));
    }

    [Fact]
    public async Task EmptyPublisherConfigurationDeniesWritesWhileLivenessRemainsAvailable()
    {
        await using var closed = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.PostConfigure<RegistryAuthOptions>(options => options.Publishers.Clear())));
        using var client = closed.CreateClient();
        PublisherTestSettings.Authorize(client);
        using var denied = await client.PostAsJsonAsync("/api/integrations", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }
}
