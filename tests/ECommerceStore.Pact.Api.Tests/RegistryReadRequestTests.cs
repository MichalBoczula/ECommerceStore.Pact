using System.Net;
using System.Text.Json;

namespace ECommerceStore.Pact.Api.Tests;

public sealed class RegistryReadRequestTests(RegistryApiFactory factory) : IClassFixture<RegistryApiFactory>
{
    [Theory]
    [InlineData("/api/applications?limit=0")]
    [InlineData("/api/applications?search=.*")]
    [InlineData("/api/applications?after=bad")]
    [InlineData("/api/applications/bad_app/versions")]
    [InlineData("/api/integrations?applicationId=bad_app")]
    [InlineData("/api/integrations?limit=101")]
    [InlineData("/api/contract-publications?kind=1")]
    [InlineData("/api/contract-publications?commitSha=abc123")]
    [InlineData("/api/contract-publications?contractSha256=abc")]
    [InlineData("/api/contract-publications?sourceRef=main")]
    [InlineData("/api/reports/verification-runs?status=0")]
    [InlineData("/api/reports/verification-runs?providerCommitSha=123")]
    [InlineData("/api/reports/verification-runs?ciAttempt=0")]
    [InlineData("/api/reports/verification-runs?ciAttempt=bad")]
    [InlineData("/api/reports/verification-runs?issueCode=")]
    [InlineData("/api/verification-profiles?name=")]
    [InlineData("/api/compatibility-decisions?profileId=bad")]
    [InlineData("/api/compatibility-decisions?profileId=00000000-0000-0000-0000-000000000000")]
    [InlineData("/api/contract-comparisons?baselineRef=main")]
    [InlineData("/api/reports/dependencies?after=bad")]
    [InlineData("/api/reports/overview?configurationSha256=bad")]
    public async Task InvalidReadQueriesFailBeforeAttemptingStorage(string path)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.TryGetProperty("errors", out _));
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task BrowseStorageFailureIsUnavailableAndDoesNotExposeConnectionDetails()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;
        using var response = await client.GetAsync("/api/reports/verification-runs?providerApplicationId=invoice");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("127.0.0.1", body); Assert.DoesNotContain("Mongo", body); Assert.DoesNotContain("Exception", body);
    }

    [Fact]
    public async Task GeneratedOpenApiDescribesAnonymousTypedGetQueriesWithCamelCaseParameters()
    {
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var paths = document.RootElement.GetProperty("paths");
        string[] expected = ["/api/applications", "/api/applications/{applicationId}/versions", "/api/integrations", "/api/contract-publications",
            "/api/reports/verification-runs", "/api/verification-profiles", "/api/compatibility-decisions", "/api/contract-comparisons",
            "/api/reports/dependencies", "/api/reports/overview"];
        foreach (var path in expected)
        {
            var operation = paths.GetProperty(path).GetProperty("get");
            Assert.True(operation.GetProperty("responses").TryGetProperty("200", out _));
            Assert.True(operation.GetProperty("responses").TryGetProperty("400", out _));
            Assert.True(operation.GetProperty("responses").TryGetProperty("503", out _));
            Assert.False(operation.TryGetProperty("requestBody", out _));
            Assert.False(operation.TryGetProperty("security", out var security) && security.GetArrayLength() > 0);
        }
        var parameters = paths.GetProperty("/api/reports/verification-runs").GetProperty("get").GetProperty("parameters")
            .EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToArray();
        Assert.Contains("consumerApplicationId", parameters); Assert.Contains("issueCode", parameters); Assert.Contains("after", parameters);
        Assert.DoesNotContain("ConsumerApplicationId", parameters);
    }
}
