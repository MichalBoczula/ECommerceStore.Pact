using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace ECommerceStore.Pact.Api.Tests;

public sealed class ContractUploadLimitTests(RegistryApiFactory factory) : IClassFixture<RegistryApiFactory>
{
    [Fact]
    public async Task RejectsContentLargerThanOneMiBBeforeContactingStorage()
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/contract-publications", new
        {
            sourceApplicationId = "payments",
            commitSha = new string('a', 40),
            kind = "ProviderOpenApi",
            sourceRef = "refs/heads/main",
            buildUrl = "https://github.com/owner/repo/actions/runs/1",
            contentBase64 = Convert.ToBase64String(new byte[1024 * 1024 + 1])
        });
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnvelopeLimitAppliesWithAndWithoutContentLength(bool streaming)
    {
        using var client = factory.CreateClient();
        var bytes = Encoding.UTF8.GetBytes(new string(' ', 2 * 1024 * 1024 + 100));
        using HttpContent content = streaming ? new StreamingContent(bytes) : new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await client.PostAsync("/api/contract-publications", content);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    private sealed class StreamingContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    }
}
