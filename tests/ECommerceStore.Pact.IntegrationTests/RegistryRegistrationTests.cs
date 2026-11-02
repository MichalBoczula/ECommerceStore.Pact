using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ECommerceStore.Pact.Api.Registry;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ECommerceStore.Pact.IntegrationTests;

[Collection("MongoDB")]
public sealed class RegistryRegistrationTests(MongoFixture mongo) : IAsyncLifetime
{
    private const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string RepositoryUrl = "https://github.com/MichalBoczula/ECommerceStorePayments";
    private const string VersionPath = "/api/applications/payments/versions";
    private readonly string _databaseName = $"pact-registry-tests-{Guid.NewGuid():N}";
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private IMongoDatabase _database = null!;

    public Task InitializeAsync()
    {
        _factory = CreateFactory();
        _client = _factory.CreateClient();
        _database = _factory.Services.GetRequiredService<IMongoDatabase>();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        try
        {
            await _database.Client.DropDatabaseAsync(_databaseName);
        }
        finally
        {
            _client.Dispose();
            await _factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task NewVersionReturns201AndCanBeReadFromItsLocation()
    {
        using var response = await _client.PostAsJsonAsync(VersionPath, VersionRequest());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var registered = (await response.Content.ReadFromJsonAsync<ApplicationVersionResultDto>())!;
        Assert.NotEqual(Guid.Empty, registered.Id);
        Assert.Equal("payments", registered.ApplicationId);
        Assert.Equal(Sha, registered.CommitSha);
        Assert.Equal(RepositoryUrl, registered.RepositoryUrl);
        Assert.Null(registered.ImageDigest);
        Assert.Equal(TimeSpan.Zero, registered.RegisteredAt.Offset);

        using var read = await _client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(registered, await read.Content.ReadFromJsonAsync<ApplicationVersionResultDto>());
        Assert.Equal(1, await Count("application_versions"));
    }

    [Fact]
    public async Task EquivalentRetryReturns200WithOriginalIdentityAndTimestamp()
    {
        using var first = await _client.PostAsJsonAsync(VersionPath, VersionRequest());
        var original = await first.Content.ReadFromJsonAsync<ApplicationVersionResultDto>();
        using var retry = await _client.PostAsJsonAsync("/api/applications/PAYMENTS/versions", VersionRequest() with
        {
            CommitSha = Sha.ToUpperInvariant(),
            RepositoryUrl = RepositoryUrl + "/"
        });
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(original, await retry.Content.ReadFromJsonAsync<ApplicationVersionResultDto>());
        Assert.Equal(1, await Count("application_versions"));
    }

    [Theory]
    [InlineData("repository")]
    [InlineData("digest")]
    public async Task MetadataConflictReturns409WithoutChangingTheStoredVersion(string difference)
    {
        using var first = await _client.PostAsJsonAsync(VersionPath, VersionRequest());
        var original = await first.Content.ReadFromJsonAsync<ApplicationVersionResultDto>();
        var conflicting = difference == "repository"
            ? VersionRequest() with { RepositoryUrl = "https://github.com/other/repo" }
            : VersionRequest() with { ImageDigest = "sha256:" + new string('b', 64) };
        using var retry = await _client.PostAsJsonAsync(VersionPath, conflicting);

        await AssertProblem(retry, HttpStatusCode.Conflict, "application_version_metadata_conflict");
        Assert.Equal(original, await _client.GetFromJsonAsync<ApplicationVersionResultDto>($"{VersionPath}/{Sha}"));
        Assert.Equal(1, await Count("application_versions"));
    }

    [Fact]
    public async Task ConcurrentDuplicatesAcrossHostsCreateExactlyOneVersion()
    {
        await using var otherHost = CreateFactory();
        using var otherClient = otherHost.CreateClient();
        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(index =>
            (index % 2 == 0 ? _client : otherClient).PostAsJsonAsync(VersionPath, VersionRequest())));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            Assert.All(responses, response => Assert.Contains(response.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Created }));
            var versions = await Task.WhenAll(responses.Select(async response =>
                (await response.Content.ReadFromJsonAsync<ApplicationVersionResultDto>())!));
            Assert.Single(versions.Select(version => version.Id).Distinct());
            Assert.Single(versions.Select(version => version.RegisteredAt).Distinct());
            Assert.Equal(1, await Count("application_versions"));
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task ConcurrentConflictingRegistrationsKeepTheWinningMetadata()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(index =>
            _client.PostAsJsonAsync(VersionPath, VersionRequest() with
            {
                RepositoryUrl = index % 2 == 0 ? RepositoryUrl : "https://github.com/other/repo"
            })));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            Assert.Equal(5, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
            var accepted = await Task.WhenAll(responses.Where(response => response.IsSuccessStatusCode)
                .Select(response => response.Content.ReadFromJsonAsync<ApplicationVersionResultDto>()));
            var winner = accepted[0];
            Assert.All(accepted, value => Assert.Equal(winner, value));

            Assert.Equal(winner, await _client.GetFromJsonAsync<ApplicationVersionResultDto>($"{VersionPath}/{Sha}"));
            Assert.Equal(1, await Count("application_versions"));
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task DifferentCommitsAndApplicationsHaveIndependentVersions()
    {
        using var first = await _client.PostAsJsonAsync(VersionPath, VersionRequest());
        using var second = await _client.PostAsJsonAsync(VersionPath, VersionRequest() with { CommitSha = new string('b', 40) });
        using var third = await _client.PostAsJsonAsync("/api/applications/invoice/versions", VersionRequest());
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(HttpStatusCode.Created, third.StatusCode);
        Assert.Equal(3, await Count("application_versions"));
        Assert.Equal(Sha, (await _client.GetFromJsonAsync<ApplicationVersionResultDto>($"{VersionPath}/{Sha}"))!.CommitSha);
    }

    [Fact]
    public async Task IntegrationIsIdempotentAndDirected()
    {
        using var first = await _client.PostAsJsonAsync("/api/integrations", IntegrationRequest());
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var original = await first.Content.ReadFromJsonAsync<IntegrationResultDto>();
        Assert.Equal("payments", original!.ConsumerApplicationId);
        Assert.Equal("invoice", original.ProviderApplicationId);
        Assert.NotEqual(Guid.Empty, original.Id);
        Assert.Equal(original, await _client.GetFromJsonAsync<IntegrationResultDto>(first.Headers.Location));

        using var retry = await _client.PostAsJsonAsync("/api/integrations", IntegrationRequest() with { ConsumerApplicationId = "PAYMENTS" });
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(original, await retry.Content.ReadFromJsonAsync<IntegrationResultDto>());
        using var reverse = await _client.PostAsJsonAsync("/api/integrations", new RegisterIntegrationRequest
        {
            ConsumerApplicationId = "invoice",
            ProviderApplicationId = "payments"
        });
        Assert.Equal(HttpStatusCode.Created, reverse.StatusCode);
        Assert.NotEqual(original.Id, (await reverse.Content.ReadFromJsonAsync<IntegrationResultDto>())!.Id);
        Assert.Equal(2, await Count("integrations"));
    }

    [Fact]
    public async Task ConcurrentIntegrationRetriesCreateOneRecord()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ =>
            _client.PostAsJsonAsync("/api/integrations", IntegrationRequest())));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            Assert.All(responses, response => Assert.Contains(response.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Created }));
            var values = await Task.WhenAll(responses.Select(async response => (await response.Content.ReadFromJsonAsync<IntegrationResultDto>())!));
            Assert.Single(values.Select(value => value.Id).Distinct());
            Assert.Single(values.Select(value => value.RegisteredAt).Distinct());
            Assert.Equal(1, await Count("integrations"));
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Theory]
    [InlineData("application_versions")]
    [InlineData("integrations")]
    public async Task UniqueIndexRejectsDuplicatesEvenOutsideTheApplication(string collectionName)
    {
        using var version = await _client.PostAsJsonAsync(VersionPath, VersionRequest());
        using var integration = await _client.PostAsJsonAsync("/api/integrations", IntegrationRequest());
        Assert.Equal(HttpStatusCode.Created, version.StatusCode);
        Assert.Equal(HttpStatusCode.Created, integration.StatusCode);
        var collection = _database.GetCollection<BsonDocument>(collectionName);
        var duplicate = (await collection.Find(FilterDefinition<BsonDocument>.Empty).SingleAsync()).DeepClone().AsBsonDocument;
        duplicate["_id"] = Guid.NewGuid().ToString("D");

        var error = await Assert.ThrowsAsync<MongoWriteException>(() => collection.InsertOneAsync(duplicate));

        Assert.Equal(ServerErrorCategory.DuplicateKey, error.WriteError.Category);
        Assert.Equal(1, await Count(collectionName));
    }

    [Theory]
    [InlineData("/api/applications/payments/versions/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("/api/integrations/payments/invoice")]
    public async Task MissingRecordsReturn404WithoutCreatingDocuments(string path)
    {
        using var response = await _client.GetAsync(path);
        await AssertProblem(response, HttpStatusCode.NotFound);
        Assert.Equal(0, await Count("application_versions"));
        Assert.Equal(0, await Count("integrations"));
    }

    [Fact]
    public async Task InvalidWriteDoesNotChangeExistingState()
    {
        using var first = await _client.PostAsJsonAsync("/api/integrations", IntegrationRequest());
        var original = await first.Content.ReadFromJsonAsync<IntegrationResultDto>();
        using var invalid = await _client.PostAsJsonAsync("/api/integrations", IntegrationRequest() with { ProviderApplicationId = "payments" });
        await AssertProblem(invalid, HttpStatusCode.BadRequest);
        Assert.Equal(original, await _client.GetFromJsonAsync<IntegrationResultDto>("/api/integrations/payments/invoice"));
        Assert.Equal(1, await Count("integrations"));
    }

    private Task<long> Count(string name) => _database.GetCollection<BsonDocument>(name)
        .CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);

    private static RegisterApplicationVersionRequest VersionRequest() => new() { CommitSha = Sha, RepositoryUrl = RepositoryUrl };
    private static RegisterIntegrationRequest IntegrationRequest() => new() { ConsumerApplicationId = "payments", ProviderApplicationId = "invoice" };

    private WebApplicationFactory<Program> CreateFactory() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mongo:ConnectionString"] = mongo.ConnectionString,
            ["Mongo:DatabaseName"] = _databaseName
        }));
    });

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode expected, string? code = null)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal((int)expected, body.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
        if (code is not null) Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    }
}
