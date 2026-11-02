using System.Net;
using System.Text;
using System.Text.Json;

namespace ECommerceStore.Pact.Api.Tests;

public sealed class RegistryRequestValidationTests(RegistryApiFactory factory) : IClassFixture<RegistryApiFactory>
{
    private const string VersionPath = "/api/applications/payments/versions";
    private const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Theory]
    [InlineData("{}", "request")]
    [InlineData("null", "request")]
    [InlineData("{", "request")]
    [InlineData("{\"commitSha\":\"main\",\"repositoryUrl\":\"https://github.com/owner/repo\"}", "commitSha")]
    [InlineData("{\"commitSha\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"repositoryUrl\":null}", "repositoryUrl")]
    [InlineData("{\"commitSha\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"repositoryUrl\":\"http://github.com/owner/repo\"}", "repositoryUrl")]
    [InlineData("{\"commitSha\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"repositoryUrl\":\"https://github.com/owner/repo\",\"imageDigest\":\"latest\"}", "imageDigest")]
    [InlineData("{\"commitSha\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"repositoryUrl\":\"https://github.com/owner/repo\",\"branch\":\"main\"}", "request")]
    public async Task InvalidVersionRequestsReturnValidationProblemsWithoutNeedingStorage(string json, string field)
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsync(VersionPath, new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = await Problem(response);
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty(field, out _));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"consumerApplicationId\":\"payments\",\"providerApplicationId\":\"PAYMENTS\"}")]
    [InlineData("{\"consumerApplicationId\":null,\"providerApplicationId\":\"invoice\"}")]
    public async Task InvalidIntegrationsReturnValidationProblems(string json)
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/api/integrations", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = await Problem(response);
        Assert.True(body.RootElement.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task UnsupportedContentTypePreserves415()
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsync(VersionPath, new StringContent("not-json", Encoding.UTF8, "text/plain"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        using var body = await Problem(response);
    }

    [Fact]
    public async Task StorageFailureReturns503WithSafeProblemDetails()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"{VersionPath}/{Sha}");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = await Problem(response);
        var json = body.RootElement.GetRawText();
        Assert.DoesNotContain("127.0.0.1", json);
        Assert.DoesNotContain("Mongo", json);
        Assert.DoesNotContain("Exception", json);
    }

    [Fact]
    public async Task OpenApiDescribesTheActualRegistrationContract()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = body.RootElement.GetProperty("paths");
        var registration = paths.GetProperty("/api/applications/{applicationId}/versions").GetProperty("post");
        Assert.Equal("RegisterApplicationVersion", registration.GetProperty("operationId").GetString());
        foreach (var status in new[] { "200", "201", "400", "409", "415", "503" })
        {
            Assert.True(registration.GetProperty("responses").TryGetProperty(status, out _));
        }

        Assert.True(paths.GetProperty("/api/integrations/{consumerApplicationId}/{providerApplicationId}")
            .GetProperty("get").GetProperty("responses").TryGetProperty("404", out _));
    }

    private static async Task<JsonDocument> Problem(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal((int)response.StatusCode, body.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
        return body;
    }
}
