using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ECommerceStore.Pact.Testing;

namespace ECommerceStore.Pact.Api.Tests;

public sealed class DeploymentProbeTests(RegistryApiFactory factory) : IClassFixture<RegistryApiFactory>
{
    [Theory]
    [InlineData("payments", true, HttpStatusCode.BadRequest)]
    [InlineData("invoice", true, HttpStatusCode.Forbidden)]
    [InlineData("payments", false, HttpStatusCode.Unauthorized)]
    public async Task DeploymentAuthProbeValidatesScopeWithoutReachingUnavailableStorage(string application, bool authenticated, HttpStatusCode expected)
    {
        using var client = factory.CreateClient();
        if (authenticated) PublisherTestSettings.Authorize(client, PublisherTestSettings.PaymentsToken);
        else client.DefaultRequestHeaders.Authorization = null;
        using var response = await client.PostAsJsonAsync($"/api/applications/{application}/versions", new
        {
            commitSha = new string('0', 40),
            repositoryUrl = "intentionally-invalid-repository-url"
        });
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.BadRequest)
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("repositoryUrl", out _));
        }
    }
}
