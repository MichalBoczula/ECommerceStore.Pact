using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace ECommerceStore.Pact.Api.Tests;

public sealed class CompatibilityRequestTests(RegistryApiFactory factory) : IClassFixture<RegistryApiFactory>
{
    [Theory]
    [InlineData("/api/verification-profiles", false)]
    [InlineData("/api/verification-profiles", true)]
    [InlineData("/api/compatibility-decisions", false)]
    [InlineData("/api/compatibility-decisions", true)]
    public async Task EnvelopesAreBoundedWithOrWithoutContentLength(string path, bool streaming)
    {
        using var client = factory.CreateClient();
        var bytes = Encoding.UTF8.GetBytes(new string(' ', 1024 * 1024 + 1));
        using HttpContent content = streaming ? new StreamingContent(bytes) : new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await client.PostAsync(path, content);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }
    [Theory]
    [InlineData("/api/verification-profiles", "{\"consumerApplicationId\":\"payments\",\"providerApplicationId\":\"invoice\",\"name\":\"required\",\"version\":\"1\",\"checks\":[]}")]
    [InlineData("/api/compatibility-decisions", "{\"sourceApplicationId\":\"invoice\",\"profileId\":\"11111111-1111-1111-1111-111111111111\",\"rows\":[]}")]
    [InlineData("/api/verification-profiles", "{\"checks\":[{\"checkId\":\"first\",\"CheckId\":\"second\"}]}")]
    public async Task EmptySelectionsAndAmbiguousDtoPropertiesAreRejected(string path, string json)
    {
        using var client = factory.CreateClient();
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(path, content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
    private sealed class StreamingContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    }
}
