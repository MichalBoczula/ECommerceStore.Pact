using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace ECommerceStore.Pact.Api.Tests;

public sealed class HistoryRequestTests(RegistryApiFactory factory) : IClassFixture<RegistryApiFactory>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ComparisonEnvelopeIsBoundedWithAndWithoutContentLength(bool streaming)
    {
        using var client = factory.CreateClient();
        var bytes = Encoding.UTF8.GetBytes(new string(' ', 1024 * 1024 + 1));
        using HttpContent content = streaming ? new StreamingContent(bytes) : new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await client.PostAsync("/api/contract-comparisons", content);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }
    [Theory]
    [InlineData("")]
    [InlineData("?beforeRunId=bad&afterRunId=bad&baselineRef=refs/heads/main")]
    [InlineData("?beforeRunId=00000000-0000-0000-0000-000000000000&afterRunId=00000000-0000-0000-0000-000000000000&baselineRef=refs/heads/main")]
    public async Task IssueHistoryRejectsMissingOrInvalidRunSelectionBeforeStorage(string query)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/issue-history" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    private sealed class StreamingContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    }
}
