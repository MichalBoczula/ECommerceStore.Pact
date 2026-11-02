using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ECommerceStore.Pact.Api.Contracts;
using ECommerceStore.Pact.Api.Verification;
using ECommerceStore.Pact.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ECommerceStore.Pact.IntegrationTests;

[Collection("MongoDB")]
public sealed class VerificationRunTests(MongoFixture mongo) : IAsyncLifetime
{
    private const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string OtherSha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string InvoiceRepository = "https://github.com/MichalBoczula/ECommerceStoreInvoice";
    private readonly string _databaseName = $"pact-runs-tests-{Guid.NewGuid():N}";
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private IMongoDatabase _database = null!;
    private ContractPublicationResultDto _publication = null!;

    public async Task InitializeAsync()
    {
        _factory = CreateFactory();
        _client = _factory.CreateClient();
        PublisherTestSettings.Authorize(_client);
        _database = _factory.Services.GetRequiredService<IMongoDatabase>();
        foreach (var (application, sha, repository) in new[] { ("payments", Sha, "https://github.com/MichalBoczula/ECommerceStorePayments"), ("invoice", Sha, InvoiceRepository), ("invoice", OtherSha, InvoiceRepository) })
        {
            using var response = await _client.PostAsJsonAsync($"/api/applications/{application}/versions", new { commitSha = sha, repositoryUrl = repository });
            response.EnsureSuccessStatusCode();
        }
        using var edge = await _client.PostAsJsonAsync("/api/integrations", new { consumerApplicationId = "payments", providerApplicationId = "invoice" });
        edge.EnsureSuccessStatusCode();
        using var published = await _client.PostAsJsonAsync("/api/contract-publications", new
        {
            sourceApplicationId = "payments",
            commitSha = Sha,
            consumerApplicationId = "payments",
            providerApplicationId = "invoice",
            kind = "ConsumerPact",
            sourceRef = "refs/heads/main",
            buildUrl = "https://github.com/MichalBoczula/ECommerceStorePayments/actions/runs/1",
            contentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("\uFEFF" + ContractSamples.Pact3))
        });
        published.EnsureSuccessStatusCode();
        _publication = (await published.Content.ReadFromJsonAsync<ContractPublicationResultDto>())!;
        PublisherTestSettings.Authorize(_client, PublisherTestSettings.InvoiceToken);
    }

    public async Task DisposeAsync()
    {
        try { await _database.Client.DropDatabaseAsync(_databaseName); }
        finally { _client.Dispose(); await _factory.DisposeAsync(); }
    }

    [Fact]
    public async Task SamePactFailsAgainstOneProviderCommitAndPassesAgainstAnotherWithoutLosingDiagnostics()
    {
        var failed = Request("Failed");
        failed["issues"]![0]!["expected"] = JsonNode.Parse("12345678901234567890.123456789");
        using var first = await Post(failed);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var old = await Read(first);
        Assert.Equal("invoice-ci", old["publisherId"]!.GetValue<string>());
        Assert.Equal("12345678901234567890.123456789", old["issues"]![0]!["expected"]!.ToJsonString());
        Assert.False(old["issues"]![0]!.AsObject().ContainsKey("actual"));
        Assert.False(old["issues"]![0]!["actualPresent"]!.GetValue<bool>());
        var passed = Request();
        passed["providerCommitSha"] = OtherSha;
        using var second = await Post(passed);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        using var anonymous = _factory.CreateClient();
        var stored = await anonymous.GetFromJsonAsync<JsonObject>(first.Headers.Location);
        Assert.True(JsonNode.DeepEquals(old, stored));
        var page = (await anonymous.GetFromJsonAsync<JsonObject>(Query()))!;
        Assert.Equal(2, page["items"]!.AsArray().Count);
        Assert.Equal(new[] { "Failed", "Passed" }, page["items"]!.AsArray().Select(item => item!["status"]!.GetValue<string>()).Order());
        Assert.Equal(2, await Count());
    }

    [Theory]
    [InlineData("Error")]
    [InlineData("Unverified")]
    public async Task InfrastructureFailureAndMissingExecutionAreStoredSeparately(string status)
    {
        using var response = await Post(Request(status));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await Read(response);
        Assert.Equal(status, result["status"]!.GetValue<string>());
        Assert.Equal(0, result["failedInteractions"]!.GetValue<int>());
        if (status == "Error") Assert.Equal("ExecutionError", result["issues"]![0]!["kind"]!.GetValue<string>());
        using var read = await _client.GetAsync(response.Headers.Location);
        Assert.True(JsonNode.DeepEquals(result, await Read(read)));
    }

    [Fact]
    public async Task RetrySurvivesRestartAndCredentialRotationButRerunHasNewIdentity()
    {
        using var first = await Post(Request());
        var original = await Read(first);
        await using var restarted = CreateFactory();
        using var retryClient = restarted.CreateClient();
        PublisherTestSettings.Authorize(retryClient); // Another authorized publisher retries identical report.
        using var retry = await retryClient.PostAsJsonAsync("/api/verification-runs", Request());
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.True(JsonNode.DeepEquals(original, await Read(retry)));
        var rerun = Request("Error");
        rerun["ciAttempt"] = 2;
        using var second = await Post(rerun);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.NotEqual(original["id"]!.ToString(), (await Read(second))["id"]!.ToString());
        Assert.Equal(2, await Count());
    }

    [Theory]
    [InlineData("status")]
    [InlineData("configurationSha256")]
    [InlineData("verifierVersion")]
    [InlineData("buildUrl")]
    [InlineData("sourceRef")]
    [InlineData("summary")]
    [InlineData("finishedAt")]
    public async Task ChangedPayloadForTheSameExecutionConflictsAndPreservesOriginal(string change)
    {
        using var first = await Post(Request());
        var original = await Read(first);
        var changed = change == "status" ? Request("Failed") : Request();
        if (change != "status") changed[change] = change switch
        {
            "configurationSha256" => new string('e', 64),
            "verifierVersion" => "2.0",
            "buildUrl" => InvoiceRepository + "/actions/runs/124",
            "sourceRef" => "refs/heads/feature",
            "finishedAt" => "2026-01-01T00:00:02Z",
            _ => "Changed summary"
        };
        using var response = await Post(changed);
        await Problem(response, HttpStatusCode.Conflict, "verification_execution_conflict");
        using var read = await _client.GetAsync(first.Headers.Location);
        Assert.True(JsonNode.DeepEquals(original, await Read(read)));
        Assert.Equal(1, await Count());
    }

    [Fact]
    public async Task CrossHostConcurrentConflictingReportsHaveOneImmutableWinner()
    {
        await using var second = CreateFactory();
        using var other = second.CreateClient();
        PublisherTestSettings.Authorize(other, PublisherTestSettings.InvoiceToken);
        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(index => (index % 2 == 0 ? _client : other)
            .PostAsJsonAsync("/api/verification-runs", Request(index % 2 == 0 ? "Passed" : "Failed"))));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            Assert.Equal(6, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
            var accepted = await Task.WhenAll(responses.Where(response => response.IsSuccessStatusCode).Select(Read));
            Assert.All(accepted, value => Assert.True(JsonNode.DeepEquals(accepted[0], value)));
            Assert.Equal(1, await Count());
            var indexes = await _database.GetCollection<BsonDocument>("verification_runs").Indexes.ListAsync();
            Assert.Contains(await indexes.ToListAsync(), index => index["name"] == "uq_verification_execution" && index["unique"] == true);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Theory]
    [InlineData("consumerPublicationId", HttpStatusCode.NotFound)]
    [InlineData("providerCommitSha", HttpStatusCode.NotFound)]
    [InlineData("consumerCommitSha", HttpStatusCode.BadRequest)]
    [InlineData("contractSha256", HttpStatusCode.BadRequest)]
    [InlineData("buildUrl", HttpStatusCode.BadRequest)]
    [InlineData("interaction", HttpStatusCode.BadRequest)]
    [InlineData("counts", HttpStatusCode.BadRequest)]
    [InlineData("issues", HttpStatusCode.BadRequest)]
    [InlineData("publisherId", HttpStatusCode.BadRequest)]
    [InlineData("status", HttpStatusCode.BadRequest)]
    public async Task InvalidReferencesAndReportsNeverCreateRuns(string field, HttpStatusCode expected)
    {
        var body = Request(field == "interaction" ? "Failed" : "Passed");
        switch (field)
        {
            case "consumerPublicationId": body[field] = Guid.NewGuid().ToString(); break;
            case "providerCommitSha": case "consumerCommitSha": body[field] = new string('f', 40); break;
            case "contractSha256": body[field] = new string('f', 64); break;
            case "buildUrl": body[field] = "https://github.com/foreign/repo/actions/runs/123"; break;
            case "interaction": body["issues"]![0]![field] = "invented interaction"; break;
            case "counts": body["verifiedInteractions"] = 0; break;
            case "issues": body[field] = null; break;
            case "status": body[field] = "0"; break;
            default: body[field] = "spoofed-ci"; break;
        }
        using var response = await Post(body);
        await Problem(response, expected);
        Assert.Equal(0, await Count());
    }

    [Fact]
    public async Task DifferentRunOrCheckCreatesSeparateExecutionAndMissingRunIs404()
    {
        foreach (var (run, check) in new[] { ("123", "pact/orders"), ("124", "pact/orders"), ("123", "pact/orders-alternate") })
        {
            var body = Request(); body["ciRunId"] = run; body["ciCheckId"] = check;
            using var response = await Post(body);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
        Assert.Equal(3, await Count());
        using var missing = await _client.GetAsync($"/api/verification-runs/{Guid.NewGuid()}");
        await Problem(missing, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ProviderOpenApiCannotBeUsedAsConsumerVerificationEvidence()
    {
        PublisherTestSettings.Authorize(_client);
        using var published = await _client.PostAsJsonAsync("/api/contract-publications", new
        {
            sourceApplicationId = "invoice",
            commitSha = Sha,
            kind = "ProviderOpenApi",
            sourceRef = "refs/heads/main",
            buildUrl = InvoiceRepository + "/actions/runs/1",
            contentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(ContractSamples.OpenApi))
        });
        published.EnsureSuccessStatusCode();
        var publication = (await published.Content.ReadFromJsonAsync<ContractPublicationResultDto>())!;
        var body = Request(); body["consumerPublicationId"] = publication.Id.ToString(); body["contractSha256"] = publication.ArtifactSha256;
        using var response = await Post(body);
        await Problem(response, HttpStatusCode.BadRequest);
        Assert.Equal(0, await Count());
    }

    [Fact]
    public async Task ConsumerCredentialCannotSubmitProviderVerification()
    {
        PublisherTestSettings.Authorize(_client, PublisherTestSettings.PaymentsToken);
        using var response = await Post(Request());
        await Problem(response, HttpStatusCode.Forbidden);
        Assert.Equal(0, await Count());
    }

    [Fact]
    public async Task HistoryPaginatesWithoutMixingIntegrationsOrHashes()
    {
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var body = Request(); body["ciAttempt"] = attempt;
            using var response = await Post(body);
            response.EnsureSuccessStatusCode();
        }
        var ids = new HashSet<string>();
        string? cursor = null;
        do
        {
            var page = (await _client.GetFromJsonAsync<JsonObject>(Query() + "&limit=1" + (cursor is null ? "" : "&after=" + cursor)))!;
            Assert.Single(page["items"]!.AsArray());
            Assert.True(ids.Add(page["items"]![0]!["id"]!.ToString()));
            cursor = page["nextCursor"]?.ToString();
        } while (cursor is not null);
        Assert.Equal(4, ids.Count);
        var empty = (await _client.GetFromJsonAsync<JsonObject>(Query().Replace(_publication.ArtifactSha256, new string('f', 64), StringComparison.Ordinal)))!;
        Assert.Empty(empty["items"]!.AsArray());
    }

    [Fact]
    public async Task ExplicitJsonNullSurvivesRoundTripAndDiagnosticObjectOrderDoesNotCauseConflict()
    {
        var body = Request("Failed");
        body["issues"]![0]!["actualPresent"] = true;
        body["issues"]![0]!["actual"] = null;
        body["issues"]![0]!["expected"] = JsonNode.Parse("{\"b\":2,\"a\":1,\"A\":3}");
        using var first = await Post(body);
        first.EnsureSuccessStatusCode();
        var result = await Read(first);
        Assert.True(result["issues"]![0]!.AsObject().ContainsKey("actual"));
        Assert.Null(result["issues"]![0]!["actual"]);
        body["issues"]![0]!["expected"] = JsonNode.Parse("{\"A\":3,\"a\":1,\"b\":2}");
        using var retry = await Post(body);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Fact]
    public async Task CorruptStoredReportReturnsServerErrorWithoutLeakingContent()
    {
        using var response = await Post(Request());
        var report = await Read(response);
        await _database.GetCollection<BsonDocument>("verification_runs").UpdateOneAsync(new BsonDocument("_id", report["id"]!.ToString()),
            new BsonDocument("$set", new BsonDocument("Summary", "synthetic-sensitive-storage-corruption")));
        using var read = await _client.GetAsync(response.Headers.Location);
        await Problem(read, HttpStatusCode.InternalServerError);
        Assert.DoesNotContain("synthetic-sensitive", await read.Content.ReadAsStringAsync());
    }

    private JsonObject Request(string status = "Passed")
    {
        var issues = status switch
        {
            "Failed" => """[{"kind":"Mismatch","code":"missing_body_field","message":"Expected totalAmount","interaction":"read an order","method":"GET","path":"/orders/123","jsonPath":"$.body.totalAmount","expected":12.34,"actualPresent":false,"evidenceUrl":"https://github.com/MichalBoczula/ECommerceStoreInvoice/actions/runs/123#step:3"}]""",
            "Error" => """[{"kind":"ExecutionError","code":"provider_unreachable","message":"Provider did not start"}]""",
            _ => "[]"
        };
        return new JsonObject
        {
            ["consumerPublicationId"] = _publication.Id.ToString(),
            ["consumerApplicationId"] = "payments",
            ["consumerCommitSha"] = Sha,
            ["providerApplicationId"] = "invoice",
            ["providerCommitSha"] = Sha,
            ["contractSha256"] = _publication.ArtifactSha256,
            ["verifierName"] = "pact-core",
            ["verifierVersion"] = "1.0",
            ["configurationSha256"] = new string('d', 64),
            ["ciRunId"] = "123",
            ["ciAttempt"] = 1,
            ["ciCheckId"] = "pact/orders",
            ["sourceRef"] = "refs/heads/main",
            ["buildUrl"] = InvoiceRepository + "/actions/runs/123",
            ["status"] = status,
            ["verifiedInteractions"] = status == "Passed" ? 1 : 0,
            ["failedInteractions"] = status == "Failed" ? 1 : 0,
            ["summary"] = "Synthetic " + status,
            ["issues"] = JsonNode.Parse(issues),
            ["evidenceUrls"] = new JsonArray(),
            ["startedAt"] = "2026-01-01T00:00:00Z",
            ["finishedAt"] = "2026-01-01T00:00:01Z"
        };
    }
    private Task<HttpResponseMessage> Post(JsonObject body) => _client.PostAsJsonAsync("/api/verification-runs", body);
    private static async Task<JsonObject> Read(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    private Task<long> Count() => _database.GetCollection<BsonDocument>("verification_runs").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
    private string Query() => $"/api/verification-runs?consumerApplicationId=payments&providerApplicationId=invoice&contractSha256={_publication.ArtifactSha256}";
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
        var body = await Read(response);
        Assert.False(string.IsNullOrWhiteSpace(body["traceId"]?.ToString()));
        if (code is not null) Assert.Equal(code, body["code"]?.ToString());
    }
}
