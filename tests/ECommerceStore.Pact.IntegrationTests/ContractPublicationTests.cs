using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ECommerceStore.Pact.Api.Contracts;
using ECommerceStore.Pact.Application;
using ECommerceStore.Pact.Application.Contracts;
using ECommerceStore.Pact.Domain.Contracts;
using ECommerceStore.Pact.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ECommerceStore.Pact.IntegrationTests;

[Collection("MongoDB")]
public sealed class ContractPublicationTests(MongoFixture mongo) : IAsyncLifetime
{
    private const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string InvoiceRepository = "https://github.com/MichalBoczula/ECommerceStoreInvoice";
    private const string PaymentsRepository = "https://github.com/MichalBoczula/ECommerceStorePayments";
    private readonly string _databaseName = $"pact-contract-tests-{Guid.NewGuid():N}";
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private IMongoDatabase _database = null!;

    public async Task InitializeAsync()
    {
        _factory = CreateFactory();
        _client = _factory.CreateClient();
        PublisherTestSettings.Authorize(_client);
        _database = _factory.Services.GetRequiredService<IMongoDatabase>();
        foreach (var (application, repository) in new[] { ("invoice", InvoiceRepository), ("payments", PaymentsRepository) })
        {
            using var registered = await _client.PostAsJsonAsync($"/api/applications/{application}/versions", new { commitSha = Sha, repositoryUrl = repository });
            registered.EnsureSuccessStatusCode();
        }
        using var edge = await _client.PostAsJsonAsync("/api/integrations", new { consumerApplicationId = "payments", providerApplicationId = "invoice" });
        edge.EnsureSuccessStatusCode();
    }

    public async Task DisposeAsync()
    {
        try { await _database.Client.DropDatabaseAsync(_databaseName); }
        finally { _client.Dispose(); await _factory.DisposeAsync(); }
    }

    [Theory]
    [InlineData("ProviderOpenApi")]
    [InlineData("ConsumerPact3")]
    [InlineData("ConsumerPact4")]
    public async Task PublishesAndDownloadsExactUtf8BytesIncludingBomAndLineEndings(string sample)
    {
        var json = sample switch { "ConsumerPact3" => ContractSamples.Pact3, "ConsumerPact4" => ContractSamples.Pact4, _ => ContractSamples.OpenApi };
        var original = Encoding.UTF8.GetBytes("\uFEFF" + json + "\r\n");
        var request = Request(sample != "ProviderOpenApi", original);
        using var response = await _client.PostAsJsonAsync("/api/contract-publications", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var published = (await response.Content.ReadFromJsonAsync<ContractPublicationResultDto>())!;
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(original)), published.ArtifactSha256);
        Assert.Equal("test-ci", published.PublisherId);
        Assert.Equal(request.SourceApplicationId, published.SourceApplicationId);
        Assert.Equal(request.SourceRef, published.SourceRef);
        using var anonymous = _factory.CreateClient();
        Assert.Equal(published, await anonymous.GetFromJsonAsync<ContractPublicationResultDto>(response.Headers.Location));
        using var download = await anonymous.GetAsync(published.ContentUrl);
        Assert.Equal(original, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal('"' + published.ArtifactSha256 + '"', download.Headers.ETag?.Tag);
        var metadata = (await anonymous.GetFromJsonAsync<ContractArtifactResultDto>($"/api/artifacts/{published.ArtifactSha256}"))!;
        Assert.Equal(original.Length, metadata.Length);
        Assert.Equal(published.Kind, metadata.Kind);
        Assert.Equal(1, await Count("contract_artifacts"));
        Assert.Equal(1, await Count("contract_versions"));
        Assert.Equal(1, await Count("contract_publications"));
    }

    [Fact]
    public async Task RetryIsIdempotentAndOtherBranchOrBuildKeepsSeparateProvenance()
    {
        var request = Request();
        using var first = await _client.PostAsJsonAsync("/api/contract-publications", request);
        var original = (await first.Content.ReadFromJsonAsync<ContractPublicationResultDto>())!;
        using var retry = await _client.PostAsJsonAsync("/api/contract-publications", request);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(original, await retry.Content.ReadFromJsonAsync<ContractPublicationResultDto>());
        foreach (var other in new[] { request with { SourceRef = "refs/heads/feature" }, request with { BuildUrl = InvoiceRepository + "/actions/runs/2" } })
        {
            using var response = await _client.PostAsJsonAsync("/api/contract-publications", other);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var published = (await response.Content.ReadFromJsonAsync<ContractPublicationResultDto>())!;
            Assert.NotEqual(original.Id, published.Id);
            Assert.Equal(original.BindingId, published.BindingId);
            Assert.Equal(original.ArtifactSha256, published.ArtifactSha256);
        }
        Assert.Equal(original, await _client.GetFromJsonAsync<ContractPublicationResultDto>(first.Headers.Location));
        Assert.Equal(1, await Count("contract_artifacts"));
        Assert.Equal(1, await Count("contract_versions"));
        Assert.Equal(3, await Count("contract_publications"));
    }

    [Fact]
    public async Task IdenticalContentCanBeReferencedFromTwoApplicationVersions()
    {
        using var first = await _client.PostAsJsonAsync("/api/contract-publications", Request());
        var original = (await first.Content.ReadFromJsonAsync<ContractPublicationResultDto>())!;
        using var version = await _client.PostAsJsonAsync("/api/applications/invoice/versions", new { commitSha = new string('b', 40), repositoryUrl = InvoiceRepository });
        version.EnsureSuccessStatusCode();
        using var second = await _client.PostAsJsonAsync("/api/contract-publications", Request() with { CommitSha = new string('b', 40) });
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var published = (await second.Content.ReadFromJsonAsync<ContractPublicationResultDto>())!;
        Assert.NotEqual(original.BindingId, published.BindingId);
        Assert.Equal(original.ArtifactSha256, published.ArtifactSha256);
        Assert.Equal(1, await Count("contract_artifacts"));
        Assert.Equal(2, await Count("contract_versions"));
        Assert.Equal(2, await Count("contract_publications"));
    }

    [Fact]
    public async Task ChangingBytesForTheSameVersionIsRejectedEvenOnAnotherBranch()
    {
        using var first = await _client.PostAsJsonAsync("/api/contract-publications", Request());
        var original = (await first.Content.ReadFromJsonAsync<ContractPublicationResultDto>())!;
        using var conflict = await _client.PostAsJsonAsync("/api/contract-publications", Request() with
        {
            SourceRef = "refs/heads/another-branch",
            ContentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(ContractSamples.OpenApi + "\n"))
        });
        await Problem(conflict, HttpStatusCode.Conflict, "contract_content_conflict");
        using var unchanged = await _client.GetAsync(original.ContentUrl);
        Assert.Equal(Encoding.UTF8.GetBytes(ContractSamples.OpenApi), await unchanged.Content.ReadAsByteArrayAsync());
        Assert.Equal(1, await Count("contract_artifacts"));
        Assert.Equal(1, await Count("contract_versions"));
        Assert.Equal(1, await Count("contract_publications"));
    }

    [Fact]
    public async Task CrossHostConcurrentRetriesCreateOneArtifactBindingAndPublication()
    {
        await using var otherHost = CreateFactory();
        using var other = otherHost.CreateClient();
        PublisherTestSettings.Authorize(other);
        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(index =>
            (index % 2 == 0 ? _client : other).PostAsJsonAsync("/api/contract-publications", Request(true))));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            Assert.All(responses, response => Assert.Contains(response.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Created }));
            var values = await Task.WhenAll(responses.Select(response => response.Content.ReadFromJsonAsync<ContractPublicationResultDto>()));
            Assert.All(values, value => Assert.Equal(values[0], value));
            Assert.Equal(1, await Count("contract_artifacts"));
            Assert.Equal(1, await Count("contract_versions"));
            Assert.Equal(1, await Count("contract_publications"));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task ConcurrentConflictingContentCannotReplaceTheWinner()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(index => _client.PostAsJsonAsync("/api/contract-publications",
            Request() with { ContentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(ContractSamples.OpenApi + (index % 2 == 0 ? "" : "\n"))) })));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            Assert.Equal(5, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
            var accepted = await Task.WhenAll(responses.Where(response => response.IsSuccessStatusCode)
                .Select(response => response.Content.ReadFromJsonAsync<ContractPublicationResultDto>()));
            Assert.All(accepted, value => Assert.Equal(accepted[0], value));
            using var read = await _client.GetAsync(accepted[0]!.ContentUrl);
            Assert.Equal(accepted[0]!.ArtifactSha256, Convert.ToHexStringLower(SHA256.HashData(await read.Content.ReadAsByteArrayAsync())));
            Assert.Equal(1, await Count("contract_versions"));
            Assert.Equal(1, await Count("contract_publications"));
            Assert.InRange(await Count("contract_artifacts"), 1, 2);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Theory]
    [InlineData("source")]
    [InlineData("integration")]
    public async Task MissingReferencesAreRejectedWithoutWritingArtifacts(string missing)
    {
        var request = missing == "source" ? Request() with { CommitSha = new string('b', 40) }
            : Request(true) with { ProviderApplicationId = "products" };
        using var response = await _client.PostAsJsonAsync("/api/contract-publications", request);
        await Problem(response, HttpStatusCode.NotFound, missing == "source" ? "application_version_not_found" : "integration_not_found");
        await NoContractWrites();
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("source")]
    [InlineData("party")]
    [InlineData("json")]
    [InlineData("base64")]
    [InlineData("provenance")]
    public async Task InvalidUploadsDoNotCreateContractRecords(string error)
    {
        var request = error switch
        {
            "kind" => Request() with { Kind = "swagger" },
            "source" => Request(true) with { SourceApplicationId = "invoice" },
            "party" => Request(true) with { ContentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(ContractSamples.Pact3.Replace("payments", "foreign", StringComparison.Ordinal))) },
            "json" => Request() with { ContentBase64 = "e30=" },
            "base64" => Request() with { ContentBase64 = "not-base64!" },
            _ => Request() with { BuildUrl = "https://github.com/other/repo/actions/runs/1" }
        };
        using var response = await _client.PostAsJsonAsync("/api/contract-publications", request);
        await Problem(response, HttpStatusCode.BadRequest);
        await NoContractWrites();
    }

    [Fact]
    public async Task AConsumerTokenCannotPublishItsProvidersOpenApi()
    {
        PublisherTestSettings.Authorize(_client, PublisherTestSettings.PaymentsToken);
        using var response = await _client.PostAsJsonAsync("/api/contract-publications", Request());
        await Problem(response, HttpStatusCode.Forbidden);
        await NoContractWrites();
    }

    [Theory]
    [InlineData("/api/artifacts/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("/api/artifacts/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/content")]
    [InlineData("/api/contract-publications/11111111-1111-1111-1111-111111111111")]
    public async Task MissingContractRecordsReturn404(string path)
    {
        using var response = await _client.GetAsync(path);
        await Problem(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RetryRecoversAfterContentAndBindingWereSavedButPublicationFailed()
    {
        await using var interruptedHost = CreateFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            var original = services.Single(descriptor => descriptor.ServiceType == typeof(IContractRepository));
            services.Remove(original);
            services.AddSingleton<IContractRepository>(provider => new FailFirstPublication(
                (IContractRepository)ActivatorUtilities.CreateInstance(provider, original.ImplementationType!)));
        }));
        using var client = interruptedHost.CreateClient();
        PublisherTestSettings.Authorize(client);
        using var failed = await client.PostAsJsonAsync("/api/contract-publications", Request());
        await Problem(failed, HttpStatusCode.ServiceUnavailable);
        Assert.Equal(1, await Count("contract_artifacts"));
        Assert.Equal(1, await Count("contract_versions"));
        Assert.Equal(0, await Count("contract_publications"));
        using var recovered = await client.PostAsJsonAsync("/api/contract-publications", Request());
        Assert.Equal(HttpStatusCode.Created, recovered.StatusCode);
        using var retry = await client.PostAsJsonAsync("/api/contract-publications", Request());
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(await recovered.Content.ReadFromJsonAsync<ContractPublicationResultDto>(), await retry.Content.ReadFromJsonAsync<ContractPublicationResultDto>());
        Assert.Equal(1, await Count("contract_artifacts"));
        Assert.Equal(1, await Count("contract_versions"));
        Assert.Equal(1, await Count("contract_publications"));
    }

    private sealed class FailFirstPublication(IContractRepository inner) : IContractRepository
    {
        private int _fail = 1;
        public Task<RegistrationResult<ContractPublication>> PublishAsync(ContractPublication publication, string repositoryUrl, CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _fail, 0) == 1) throw new RegistryUnavailableException(new TimeoutException("Synthetic publication interruption."));
            return inner.PublishAsync(publication, repositoryUrl, cancellationToken);
        }
        public Task<RegistrationResult<ContractBinding>> BindAsync(ContractArtifact artifact, ContractBinding binding, CancellationToken cancellationToken) => inner.BindAsync(artifact, binding, cancellationToken);
        public Task<ContractArtifact?> GetArtifactAsync(string hash, CancellationToken cancellationToken) => inner.GetArtifactAsync(hash, cancellationToken);
        public Task<ContractBinding?> GetBindingAsync(Guid id, CancellationToken cancellationToken) => inner.GetBindingAsync(id, cancellationToken);
        public Task<ContractPublication?> GetPublicationAsync(Guid id, CancellationToken cancellationToken) => inner.GetPublicationAsync(id, cancellationToken);
    }

    private static PublishContractRequest Request(bool pact = false, byte[]? bytes = null) => new()
    {
        SourceApplicationId = pact ? "payments" : "invoice",
        CommitSha = Sha,
        Kind = pact ? "ConsumerPact" : "ProviderOpenApi",
        ConsumerApplicationId = pact ? "payments" : null,
        ProviderApplicationId = pact ? "invoice" : null,
        SourceRef = "refs/heads/main",
        BuildUrl = (pact ? PaymentsRepository : InvoiceRepository) + "/actions/runs/1",
        ContentBase64 = Convert.ToBase64String(bytes ?? Encoding.UTF8.GetBytes(pact ? ContractSamples.Pact3 : ContractSamples.OpenApi))
    };

    private Task<long> Count(string collection) => _database.GetCollection<BsonDocument>(collection).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
    private async Task NoContractWrites()
    {
        Assert.Equal(0, await Count("contract_artifacts"));
        Assert.Equal(0, await Count("contract_versions"));
        Assert.Equal(0, await Count("contract_publications"));
    }
    private WebApplicationFactory<Program> CreateFactory() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mongo:ConnectionString"] = mongo.ConnectionString,
            ["Mongo:DatabaseName"] = _databaseName
        }.Concat(PublisherTestSettings.Configuration)));
    });
    private static async Task Problem(HttpResponseMessage response, HttpStatusCode expected, string? code = null)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("traceId").GetString()));
        if (code is not null) Assert.Equal(code, document.RootElement.GetProperty("code").GetString());
    }
}
