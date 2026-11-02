using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using ECommerceStore.Pact.Api.Contracts;
using ECommerceStore.Pact.Api.History;
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
public sealed class ComparisonHistoryTests(MongoFixture mongo) : IAsyncLifetime
{
    private const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string NextSha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string InvoiceRepository = "https://github.com/MichalBoczula/ECommerceStoreInvoice";
    private const string PaymentsRepository = "https://github.com/MichalBoczula/ECommerceStorePayments";
    private readonly string _databaseName = "pact-history-tests-" + Guid.NewGuid().ToString("N");
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private IMongoDatabase _database = null!;
    private ContractPublicationResultDto _providerBefore = null!;
    private ContractPublicationResultDto _consumer = null!;

    public async Task InitializeAsync()
    {
        _factory = Factory(); _client = Client(_factory);
        _database = _factory.Services.GetRequiredService<IMongoDatabase>();
        foreach (var (app, repository) in new[] { ("invoice", InvoiceRepository), ("payments", PaymentsRepository) })
            foreach (var sha in new[] { Sha, NextSha })
            {
                using var registered = await _client.PostAsJsonAsync($"/api/applications/{app}/versions", new { commitSha = sha, repositoryUrl = repository });
                registered.EnsureSuccessStatusCode();
            }
        using var edge = await _client.PostAsJsonAsync("/api/integrations", new { consumerApplicationId = "payments", providerApplicationId = "invoice" });
        edge.EnsureSuccessStatusCode();
        _providerBefore = await Publish(false, Sha, ComparisonSamples.OpenApi().ToJsonString());
        _consumer = await Publish(true, Sha, ContractSamples.Pact3);
    }
    public async Task DisposeAsync()
    {
        try { await _database.Client.DropDatabaseAsync(_databaseName); }
        finally { _client.Dispose(); await _factory.DisposeAsync(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SchemaSnapshotDistinguishesAdditiveResponseFromRequiredFieldRemoval(bool removal)
    {
        var after = ComparisonSamples.OpenApi();
        if (removal)
        {
            ComparisonSamples.Response(after)["properties"]!.AsObject().Remove("totalAmount");
            ComparisonSamples.Response(after)["required"] = new JsonArray();
        }
        else ComparisonSamples.Response(after)["properties"]!["currency"] = new JsonObject { ["type"] = "string" };
        var published = await Publish(false, NextSha, after.ToJsonString(), "refs/heads/feature");
        PublisherTestSettings.Authorize(_client, PublisherTestSettings.InvoiceToken);
        using var response = await _client.PostAsJsonAsync("/api/contract-comparisons", Request(published));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var report = (await response.Content.ReadFromJsonAsync<ContractComparisonResultDto>())!;
        Assert.Equal(removal ? "PotentiallyBreaking" : "NoBreakingChangesDetected", report.SchemaAssessment);
        Assert.True(report.ContentChanged); Assert.True(report.JsonChanged);
        Assert.Equal("NotEvaluated", report.ConsumerImpact);
        Assert.Equal(Sha, report.Before.CommitSha); Assert.Equal(NextSha, report.After.CommitSha);
        Assert.Equal("refs/heads/main", report.BaselineRef); Assert.Equal("refs/heads/feature", report.After.SourceRef);
        Assert.Equal("invoice-ci", report.PublisherId);
        if (removal) Assert.Contains(report.Findings, finding => finding.Code == "response-required-property-removed");
        using var anonymous = _factory.CreateClient();
        var original = await response.Content.ReadAsStringAsync();
        Assert.Equal(original, await anonymous.GetStringAsync(response.Headers.Location));
        Assert.Equal(1, await CountComparisons());
    }

    [Fact]
    public async Task ByteOnlyChangesAreSeparateFromJsonAndSchemaChanges()
    {
        var after = await Publish(false, NextSha, "\uFEFF" + ComparisonSamples.OpenApi().ToJsonString() + "\r\n");
        using var response = await _client.PostAsJsonAsync("/api/contract-comparisons", Request(after));
        response.EnsureSuccessStatusCode();
        var report = (await response.Content.ReadFromJsonAsync<ContractComparisonResultDto>())!;
        Assert.True(report.ContentChanged); Assert.False(report.JsonChanged);
        Assert.Equal("NoBreakingChangesDetected", report.SchemaAssessment); Assert.Empty(report.Findings);
    }

    [Fact]
    public async Task CrossHostRetriesKeepOriginalSnapshotEvenIfToolLaterBecomesUnavailable()
    {
        var after = await Publish(false, NextSha, ComparisonSamples.OpenApi().ToJsonString());
        var request = Request(after);
        await using var otherFactory = Factory();
        using var other = Client(otherFactory);
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => (i % 2 == 0 ? other : _client).PostAsJsonAsync("/api/contract-comparisons", request)));
        try
        {
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
            Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode));
            var results = await Task.WhenAll(responses.Select(r => r.Content.ReadAsStringAsync()));
            Assert.All(results, value => Assert.Equal(results[0], value));
            await using var offline = Factory("/missing/oasdiff");
            using var retryClient = Client(offline);
            using var retry = await retryClient.PostAsJsonAsync("/api/contract-comparisons", request);
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            Assert.Equal(results[0], await retry.Content.ReadAsStringAsync());
            Assert.Equal(1, await CountComparisons());
            var indexes = await _database.GetCollection<BsonDocument>("contract_comparisons").Indexes.ListAsync();
            Assert.Contains(await indexes.ToListAsync(), value => value["name"] == "uq_contract_comparison" && value["unique"] == true);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task ToolFailureIsUnavailableAndDoesNotPersistFalseSafeComparison()
    {
        var after = await Publish(false, NextSha, ComparisonSamples.OpenApi().ToJsonString());
        await using var offline = Factory("/missing/oasdiff");
        using var client = Client(offline);
        using var response = await client.PostAsJsonAsync("/api/contract-comparisons", Request(after));
        await Problem(response, HttpStatusCode.ServiceUnavailable);
        Assert.DoesNotContain("/missing", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, await CountComparisons());
    }

    [Theory]
    [InlineData("baseline", HttpStatusCode.BadRequest)]
    [InlineData("missing", HttpStatusCode.NotFound)]
    [InlineData("kind", HttpStatusCode.BadRequest)]
    [InlineData("owner", HttpStatusCode.BadRequest)]
    [InlineData("scope", HttpStatusCode.Forbidden)]
    [InlineData("integration", HttpStatusCode.BadRequest)]
    public async Task InvalidOrUnauthorizedComparisonCannotWrite(string problem, HttpStatusCode status)
    {
        var after = await Publish(false, NextSha, ComparisonSamples.OpenApi().ToJsonString());
        var request = Request(after);
        request = problem switch
        {
            "baseline" => request with { BaselineRef = "refs/heads/other" },
            "missing" => request with { BeforePublicationId = Guid.NewGuid() },
            "kind" => request with { AfterPublicationId = _consumer.Id },
            "owner" => request with { SourceApplicationId = "payments" },
            "integration" => request with { ProviderApplicationId = "products" },
            _ => request
        };
        if (problem == "scope") PublisherTestSettings.Authorize(_client, PublisherTestSettings.PaymentsToken);
        using var response = await _client.PostAsJsonAsync("/api/contract-comparisons", request);
        await Problem(response, status);
        Assert.Equal(0, await CountComparisons());
    }

    [Fact]
    public async Task PersistingFailureAndConfirmedFixRemainLinkedToOriginalEvidence()
    {
        var original = await Run("Failed");
        var retry = await Run("Failed", attempt: 2);
        var repeated = await History(original, retry);
        Assert.Equal("Persisting", Assert.Single(repeated.Issues).Transition);
        var fixedRun = await Run("Passed", providerSha: NextSha);
        var fixedHistory = await History(original, fixedRun);
        Assert.True(fixedHistory.Comparable);
        Assert.Equal("Resolved", Assert.Single(fixedHistory.Issues).Transition);
        Assert.Equal("Failed", fixedHistory.Before.Status); Assert.Equal("Passed", fixedHistory.After.Status);
        Assert.Equal("$.body.totalAmount", fixedHistory.Issues[0].Before!.JsonPath);
        using var anonymous = _factory.CreateClient();
        var preserved = (await anonymous.GetFromJsonAsync<VerificationRunResultDto>($"/api/verification-runs/{original.Id}"))!;
        Assert.Equal("Failed", preserved.Status); Assert.Single(preserved.Issues);
    }

    [Fact]
    public async Task DroppingAConsumerRequirementIsNotAProviderFixOrSchemaCompatibilityResult()
    {
        var original = await Run("Failed");
        var changedPact = JsonNode.Parse(ContractSamples.Pact3)!;
        changedPact["interactions"]![0]!["response"]!["body"]!.AsObject().Remove("totalAmount");
        var updated = await Publish(true, NextSha, changedPact.ToJsonString());
        var passed = await Run("Passed", consumer: updated, providerSha: NextSha);
        var history = await History(original, passed);
        Assert.False(history.Comparable);
        Assert.Equal("RequirementsChanged", Assert.Single(history.Issues).Transition);
        var request = Request(updated) with { SourceApplicationId = "payments", BeforePublicationId = _consumer.Id };
        PublisherTestSettings.Authorize(_client, PublisherTestSettings.PaymentsToken);
        using var response = await _client.PostAsJsonAsync("/api/contract-comparisons", request);
        response.EnsureSuccessStatusCode();
        var comparison = (await response.Content.ReadFromJsonAsync<ContractComparisonResultDto>())!;
        Assert.Equal("registry-json", comparison.Tool); Assert.Equal("NotAssessed", comparison.SchemaAssessment);
        Assert.Equal("NotEvaluated", comparison.ConsumerImpact); Assert.True(comparison.JsonChanged);
    }

    [Theory]
    [InlineData("Error", false, "Unconfirmed")]
    [InlineData("Unverified", false, "Unconfirmed")]
    [InlineData("Passed", true, "VerifierChanged")]
    public async Task MissingOrChangedVerificationCannotResolveIssue(string status, bool configurationChanged, string transition)
    {
        var before = await Run("Failed");
        var after = await Run(status, providerSha: NextSha, configurationChanged: configurationChanged);
        var history = await History(before, after);
        Assert.Equal(transition, Assert.Single(history.Issues).Transition);
        Assert.DoesNotContain(history.Issues, issue => issue.Transition == "Resolved");
    }

    [Fact]
    public async Task HistoryAndSnapshotRequireExistingExplicitReferences()
    {
        using var missing = await _client.GetAsync($"/api/issue-history?beforeRunId={Guid.NewGuid()}&afterRunId={Guid.NewGuid()}&baselineRef=refs/heads/main");
        await Problem(missing, HttpStatusCode.NotFound);
        var before = await Run("Failed"); var after = await Run("Passed", providerSha: NextSha);
        using var wrongRef = await _client.GetAsync($"/api/issue-history?beforeRunId={before.Id}&afterRunId={after.Id}&baselineRef=refs/heads/other");
        await Problem(wrongRef, HttpStatusCode.BadRequest);
        using var comparison = await _client.GetAsync($"/api/contract-comparisons/{Guid.NewGuid()}");
        await Problem(comparison, HttpStatusCode.NotFound);
    }

    private CompareContractsRequest Request(ContractPublicationResultDto after) => new()
    {
        SourceApplicationId = "invoice",
        ConsumerApplicationId = "payments",
        ProviderApplicationId = "invoice",
        BeforePublicationId = _providerBefore.Id,
        AfterPublicationId = after.Id,
        BaselineRef = "refs/heads/main"
    };
    private async Task<ContractPublicationResultDto> Publish(bool consumer, string sha, string json, string sourceRef = "refs/heads/main")
    {
        using var response = await _client.PostAsJsonAsync("/api/contract-publications", new
        {
            sourceApplicationId = consumer ? "payments" : "invoice",
            commitSha = sha,
            kind = consumer ? "ConsumerPact" : "ProviderOpenApi",
            consumerApplicationId = consumer ? "payments" : null,
            providerApplicationId = consumer ? "invoice" : null,
            sourceRef,
            buildUrl = (consumer ? PaymentsRepository : InvoiceRepository) + "/actions/runs/1",
            contentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ContractPublicationResultDto>())!;
    }
    private async Task<VerificationRunResultDto> Run(string status, int attempt = 1, string providerSha = Sha, ContractPublicationResultDto? consumer = null, bool configurationChanged = false)
    {
        consumer ??= _consumer;
        var issues = status switch
        {
            "Failed" => JsonNode.Parse("""[{"kind":"Mismatch","code":"missing_field","message":"Missing amount","interaction":"read an order","method":"GET","path":"/orders/123","jsonPath":"$.body.totalAmount","expected":12.34,"actualPresent":false}]"""),
            "Error" => JsonNode.Parse("""[{"kind":"ExecutionError","code":"startup_timeout","message":"Provider unavailable"}]"""),
            _ => new JsonArray()
        };
        using var response = await _client.PostAsJsonAsync("/api/verification-runs", new
        {
            consumerPublicationId = consumer.Id,
            consumerApplicationId = "payments",
            consumerCommitSha = consumer.CommitSha,
            providerApplicationId = "invoice",
            providerCommitSha = providerSha,
            contractSha256 = consumer.ArtifactSha256,
            verifierName = "pact-core",
            verifierVersion = "1",
            configurationSha256 = new string(configurationChanged ? 'e' : 'd', 64),
            ciRunId = "123",
            ciAttempt = attempt,
            ciCheckId = "pact/orders",
            sourceRef = "refs/heads/main",
            buildUrl = InvoiceRepository + "/actions/runs/123",
            status,
            verifiedInteractions = status == "Passed" ? 1 : 0,
            failedInteractions = status == "Failed" ? 1 : 0,
            summary = "Synthetic report",
            issues,
            evidenceUrls = Array.Empty<string>(),
            startedAt = "2026-01-01T00:00:00Z",
            finishedAt = "2026-01-01T00:00:01Z"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<VerificationRunResultDto>())!;
    }
    private async Task<IssueHistoryResultDto> History(VerificationRunResultDto before, VerificationRunResultDto after) =>
        (await _client.GetFromJsonAsync<IssueHistoryResultDto>($"/api/issue-history?beforeRunId={before.Id}&afterRunId={after.Id}&baselineRef=refs/heads/main"))!;
    private Task<long> CountComparisons() => _database.GetCollection<BsonDocument>("contract_comparisons").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
    private WebApplicationFactory<Program> Factory(string? executable = null) => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mongo:ConnectionString"] = mongo.ConnectionString,
            ["Mongo:DatabaseName"] = _databaseName,
            ["Comparison:Executable"] = executable ?? Environment.GetEnvironmentVariable("OASDIFF_PATH") ?? "oasdiff"
        }.Concat(PublisherTestSettings.Configuration)));
    });
    private static HttpClient Client(WebApplicationFactory<Program> factory)
    { var client = factory.CreateClient(); PublisherTestSettings.Authorize(client); return client; }
    private static async Task Problem(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
