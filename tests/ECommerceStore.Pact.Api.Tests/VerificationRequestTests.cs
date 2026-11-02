using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace ECommerceStore.Pact.Api.Tests;

public sealed class VerificationRequestTests(RegistryApiFactory factory) : IClassFixture<RegistryApiFactory>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StreamedAndKnownLengthEnvelopesAreBounded(bool streaming)
    {
        using var client = factory.CreateClient();
        var bytes = Encoding.UTF8.GetBytes(new string(' ', 1024 * 1024 + 1));
        using HttpContent content = streaming ? new StreamingContent(bytes) : new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await client.PostAsync("/api/verification-runs", content);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Theory]
    [InlineData("{}", "application/json", HttpStatusCode.BadRequest)]
    [InlineData("{\"status\":\"Passed\",\"Status\":\"Failed\"}", "application/json", HttpStatusCode.BadRequest)]
    [InlineData("{}", "text/plain", HttpStatusCode.UnsupportedMediaType)]
    [InlineData("{}", "application/json; charset=utf-16", HttpStatusCode.UnsupportedMediaType)]
    public async Task InvalidEnvelopeReturnsProblemDetailsBeforeStorage(string body, string contentType, HttpStatusCode expected)
    {
        using var client = factory.CreateClient();
        using var content = new StringContent(body);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        using var response = await client.PostAsync("/api/verification-runs", content);
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("?consumerApplicationId=payments&providerApplicationId=invoice&contractSha256=bad")]
    [InlineData("?consumerApplicationId=payments&providerApplicationId=invoice&contractSha256=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa&limit=101")]
    [InlineData("")]
    public async Task HistoryRequiresExplicitValidScopeAndBoundedPage(string query)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/verification-runs" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed class StreamingContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    }
}
