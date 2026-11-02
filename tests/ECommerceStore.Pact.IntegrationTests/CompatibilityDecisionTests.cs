using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using ECommerceStore.Pact.Api.Compatibility;
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
public sealed class CompatibilityDecisionTests(MongoFixture mongo) : IAsyncLifetime
{
    private const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string NextSha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string InvoiceRepository = "https://github.com/MichalBoczula/ECommerceStoreInvoice";
    private const string PaymentsRepository = "https://github.com/MichalBoczula/ECommerceStorePayments";
    private readonly string _databaseName = "pact-decisions-" + Guid.NewGuid().ToString("N");
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private IMongoDatabase _database = null!;
    private ContractPublicationResultDto _consumer = null!;
    private VerificationProfileResultDto _profile = null!;

    public async Task InitializeAsync()
    {
        _factory = Factory(); _client = Client(_factory); _database = _factory.Services.GetRequiredService<IMongoDatabase>();
        foreach (var (app, repository) in new[] { ("payments", PaymentsRepository), ("invoice", InvoiceRepository) })
            foreach (var sha in new[] { Sha, NextSha })
            {
                using var response = await _client.PostAsJsonAsync($"/api/applications/{app}/versions", new { commitSha = sha, repositoryUrl = repository });
                response.EnsureSuccessStatusCode();
            }
        using var edge = await _client.PostAsJsonAsync("/api/integrations", new { consumerApplicationId = "payments", providerApplicationId = "invoice" });
        edge.EnsureSuccessStatusCode();
        _consumer = await PublishPact(Sha);
        using var registered = await _client.PostAsJsonAsync("/api/verification-profiles", ProfileRequest());
        registered.EnsureSuccessStatusCode(); _profile = (await registered.Content.ReadFromJsonAsync<VerificationProfileResultDto>())!;
    }
    public async Task DisposeAsync()
    {
        try { await _database.Client.DropDatabaseAsync(_databaseName); }
        finally { _client.Dispose(); await _factory.DisposeAsync(); }
    }

    [Fact]
    public async Task SamePactFailsAndPassesIndependentlyAcrossProviderVersions()
    {
        var failed = await Run("Failed"); var passed = await Run("Passed", providerSha: NextSha);
        using var response = await Evaluate(Request([Row(), Row(NextSha)]));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var decision = (await response.Content.ReadFromJsonAsync<CompatibilityDecisionResultDto>())!;
        Assert.Equal("explicit-ci-execution-v1", decision.Policy); Assert.Equal("Failed", decision.Status); Assert.False(decision.Compatible);
        var old = decision.Rows.Single(row => row.ProviderCommitSha == Sha); var next = decision.Rows.Single(row => row.ProviderCommitSha == NextSha);
        Assert.Equal("Failed", old.Status); Assert.Equal("Passed", next.Status);
        Assert.Equal(failed.Id, old.Checks[0].EvidenceId); Assert.Equal(passed.Id, next.Checks[0].EvidenceId);
        Assert.Single(old.UnresolvedChecks); Assert.Empty(next.UnresolvedChecks);
        using var anonymous = _factory.CreateClient();
        Assert.Equal(await response.Content.ReadAsStringAsync(), await anonymous.GetStringAsync(response.Headers.Location));
        var evidence = (await anonymous.GetFromJsonAsync<VerificationRunResultDto>(old.Checks[0].EvidenceUrl))!;
        Assert.Equal("Failed", evidence.Status); Assert.Equal("$.body.totalAmount", Assert.Single(evidence.Issues).JsonPath);
    }

    [Fact]
    public async Task MissingRequiredCheckCreatesNewSnapshotWhenEvidenceArrivesWithoutChangingOldDecision()
    {
        using var registration = await _client.PostAsJsonAsync("/api/verification-profiles", ProfileRequest(twoChecks: true) with { Version = "2" });
        registration.EnsureSuccessStatusCode(); var profile = (await registration.Content.ReadFromJsonAsync<VerificationProfileResultDto>())!;
        var request = Request() with { ProfileId = profile.Id };
        await Run("Passed");
        using var first = await Evaluate(request); var before = await Read(first);
        Assert.Equal("Unverified", before.Status); Assert.False(before.Compatible);
        Assert.Equal("pact/serializer", Assert.Single(before.Rows[0].MissingChecks));
        await Run("Passed", checkId: "pact/serializer");
        using var second = await Evaluate(request); var after = await Read(second);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode); Assert.True(after.Compatible); Assert.Equal("Passed", after.Status); Assert.NotEqual(before.Id, after.Id);
        using var retry = await Evaluate(request);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode); Assert.Equal(after.Id, (await Read(retry)).Id);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await _client.GetStringAsync(first.Headers.Location));
    }

    [Fact]
    public async Task ExplicitRerunCannotEraseFailureOrBorrowResultsFromAnotherAttempt()
    {
        var failed = await Run("Failed"); await Run("Passed", attempt: 2);
        using var oldResponse = await Evaluate(Request()); var old = await Read(oldResponse);
        Assert.Equal("Failed", old.Status); Assert.Equal(failed.Id, old.Rows[0].Checks[0].EvidenceId);
        using var rerun = await Evaluate(Request([Row() with { CiAttempt = 2 }])); Assert.True((await Read(rerun)).Compatible);
        using var missing = await Evaluate(Request([Row() with { CiRunId = "unknown-run" }]));
        Assert.Equal("Unverified", (await Read(missing)).Status);
    }

    [Theory]
    [InlineData("verifier", "VerifierMismatch")]
    [InlineData("version", "VerifierMismatch")]
    [InlineData("configuration", "VerifierMismatch")]
    [InlineData("ref", "SourceRefMismatch")]
    public async Task InapplicablePassedReportIsVisibleButCannotPassTheDecision(string change, string reason)
    {
        var run = await Run("Passed", change: change);
        using var response = await Evaluate(Request()); var decision = await Read(response);
        Assert.Equal("Unverified", decision.Status); Assert.False(decision.Compatible);
        var check = Assert.Single(decision.Rows[0].Checks);
        Assert.Equal(reason, check.Reason); Assert.Equal("Passed", check.ReportedStatus); Assert.Equal(run.Id, check.EvidenceId);
    }

    [Theory]
    [InlineData("Error")]
    [InlineData("Unverified")]
    public async Task InfrastructureErrorAndUnverifiedRemainDistinctFromContractFailure(string status)
    {
        await Run(status);
        using var response = await Evaluate(Request()); var decision = await Read(response);
        Assert.Equal(status, decision.Status); Assert.False(decision.Compatible);
        Assert.Single(decision.Rows[0].UnresolvedChecks);
    }

    [Fact]
    public async Task NewProviderNeedsItsOwnEvidenceEvenWhenPactContentIsIdentical()
    {
        await Run("Passed");
        using var response = await Evaluate(Request([Row(NextSha)])); var result = await Read(response);
        Assert.False(result.Compatible); Assert.Equal("MissingExecution", result.Rows[0].Checks[0].Reason); Assert.Null(result.Rows[0].Checks[0].EvidenceId);
    }

    [Fact]
    public async Task MissingConsumerPactIsUnverifiedAndLaterPublicationDoesNotRewriteOriginalSnapshot()
    {
        var request = Request([Row() with { ConsumerCommitSha = NextSha }]);
        using var first = await Evaluate(request); var missing = await Read(first);
        Assert.Equal("ContractNotPublished", missing.Rows[0].Checks[0].Reason); Assert.Null(missing.Rows[0].BindingId);
        var consumer = await PublishPact(NextSha); await Run("Passed", consumer: consumer);
        using var second = await Evaluate(request); var passed = await Read(second);
        Assert.True(passed.Compatible); Assert.NotEqual(missing.Id, passed.Id);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await _client.GetStringAsync(first.Headers.Location));
    }

    [Fact]
    public async Task ConcurrentDecisionsHaveOneIdentityForEachObservedEvidenceSet()
    {
        await using var otherFactory = Factory(); using var other = Client(otherFactory);
        var request = Request();
        var pending = Task.WhenAll(Enumerable.Range(0, 10).Select(i => (i % 2 == 0 ? _client : other).PostAsJsonAsync("/api/compatibility-decisions", request)));
        await Run("Passed");
        var responses = await pending;
        try
        {
            var values = await Task.WhenAll(responses.Select(Read));
            Assert.All(values, value => Assert.Contains(value.Status, new[] { "Unverified", "Passed" }));
            foreach (var group in values.GroupBy(value => value.Status)) Assert.Single(group.Select(value => value.Id).Distinct());
            using var final = await Evaluate(request); Assert.True((await Read(final)).Compatible);
            Assert.InRange(await Count("compatibility_decisions"), 1, 2);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task UnrelatedConcurrentPublicationsAndRunsDoNotChangeSelectedDecision()
    {
        await Run("Failed"); using var initial = await Evaluate(Request()); var original = await Read(initial);
        await Task.WhenAll(PublishPact(NextSha), Run("Passed", attempt: 2), Run("Passed", providerSha: NextSha));
        using var retried = await Evaluate(Request());
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode); var repeated = await Read(retried);
        Assert.Equal(original.Id, repeated.Id); Assert.Equal("Failed", repeated.Status);
    }

    [Fact]
    public async Task ProfileRetryIsCanonicalAndChangedChecksRequireNewVersion()
    {
        var request = ProfileRequest();
        PublisherTestSettings.Authorize(_client, PublisherTestSettings.PaymentsToken);
        using var retry = await _client.PostAsJsonAsync("/api/verification-profiles", request);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode); Assert.Equal(_profile.Id, (await retry.Content.ReadFromJsonAsync<VerificationProfileResultDto>())!.Id);
        var changed = request with { Checks = [Check("pact/other")] };
        using var conflict = await _client.PostAsJsonAsync("/api/verification-profiles", changed);
        await Problem(conflict, HttpStatusCode.Conflict);
        using var next = await _client.PostAsJsonAsync("/api/verification-profiles", changed with { Version = "2" });
        Assert.Equal(HttpStatusCode.Created, next.StatusCode);
        var old = (await _client.GetFromJsonAsync<VerificationProfileResultDto>($"/api/verification-profiles/{_profile.Id}"))!;
        Assert.Equal("pact/orders", Assert.Single(old.Checks).CheckId);
    }

    [Fact]
    public async Task ConcurrentConflictingProfileRegistrationHasOneImmutableWinner()
    {
        await using var secondFactory = Factory(); using var second = Client(secondFactory);
        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => (i % 2 == 0 ? _client : second).PostAsJsonAsync("/api/verification-profiles",
            ProfileRequest() with { Name = "race", Checks = [Check(i % 2 == 0 ? "pact/orders" : "pact/other")] })));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            Assert.Equal(6, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
            var accepted = await Task.WhenAll(responses.Where(response => response.IsSuccessStatusCode).Select(response => response.Content.ReadFromJsonAsync<VerificationProfileResultDto>()));
            Assert.Single(accepted.Select(value => value!.Id).Distinct());
            Assert.Equal(2, await Count("verification_profiles"));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task ProviderCannotDefineConsumerChecksButBothSidesCanEvaluateTheirIntegration()
    {
        PublisherTestSettings.Authorize(_client, PublisherTestSettings.InvoiceToken);
        using var denied = await _client.PostAsJsonAsync("/api/verification-profiles", ProfileRequest() with { Version = "2" });
        await Problem(denied, HttpStatusCode.Forbidden);
        using var provider = await Evaluate(Request()); Assert.Equal(HttpStatusCode.Created, provider.StatusCode);
        PublisherTestSettings.Authorize(_client, PublisherTestSettings.PaymentsToken);
        using var consumer = await Evaluate(Request() with { SourceApplicationId = "payments" });
        Assert.Equal(HttpStatusCode.OK, consumer.StatusCode); Assert.Equal((await Read(provider)).Id, (await Read(consumer)).Id);
        using var wrongScope = await Evaluate(Request()); await Problem(wrongScope, HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("hash", HttpStatusCode.BadRequest)]
    [InlineData("version", HttpStatusCode.NotFound)]
    [InlineData("profile", HttpStatusCode.NotFound)]
    [InlineData("ref", HttpStatusCode.BadRequest)]
    [InlineData("duplicate", HttpStatusCode.BadRequest)]
    [InlineData("source", HttpStatusCode.BadRequest)]
    public async Task InvalidSelectionsNeverPersistDecisions(string error, HttpStatusCode status)
    {
        var request = error switch
        {
            "hash" => Request([Row() with { ContractSha256 = new string('f', 64) }]),
            "version" => Request([Row() with { ProviderCommitSha = new string('f', 40) }]),
            "profile" => Request() with { ProfileId = Guid.NewGuid() },
            "ref" => Request([Row() with { ProviderSourceRef = "main" }]),
            "duplicate" => Request([Row(), Row() with { ConsumerCommitSha = Sha.ToUpperInvariant() }]),
            _ => Request() with { SourceApplicationId = "products" }
        };
        using var response = await Evaluate(request); await Problem(response, status);
        Assert.Equal(0, await Count("compatibility_decisions"));
    }

    [Fact]
    public async Task CorruptStoredDecisionDoesNotBecomePassingEvidence()
    {
        using var first = await Evaluate(Request()); var decision = await Read(first);
        await _database.GetCollection<BsonDocument>("compatibility_decisions").UpdateOneAsync(new BsonDocument("_id", decision.Id.ToString("D")),
            new BsonDocument("$set", new BsonDocument("Rows.0.Checks.0.Status", "Passed")));
        using var read = await _client.GetAsync(first.Headers.Location); await Problem(read, HttpStatusCode.InternalServerError);
    }

    private RegisterVerificationProfileRequest ProfileRequest(bool twoChecks = false) => new()
    {
        ConsumerApplicationId = "payments",
        ProviderApplicationId = "invoice",
        Name = "required",
        Version = "1",
        Checks = twoChecks ? [Check("pact/orders"), Check("pact/serializer")] : [Check("pact/orders")]
    };
    private static RequiredCheckRequest Check(string id) => new() { CheckId = id, VerifierName = "pact-core", VerifierVersion = "1", ConfigurationSha256 = new string('d', 64) };
    private CompatibilityRowRequest Row(string providerSha = Sha) => new()
    { ConsumerCommitSha = Sha, ProviderCommitSha = providerSha, ContractSha256 = _consumer.ArtifactSha256, CiRunId = "123", CiAttempt = 1, ProviderSourceRef = "refs/heads/main" };
    private EvaluateCompatibilityRequest Request(IReadOnlyList<CompatibilityRowRequest>? rows = null) => new() { SourceApplicationId = "invoice", ProfileId = _profile.Id, Rows = rows ?? [Row()] };
    private Task<HttpResponseMessage> Evaluate(EvaluateCompatibilityRequest request) => _client.PostAsJsonAsync("/api/compatibility-decisions", request);
    private static async Task<CompatibilityDecisionResultDto> Read(HttpResponseMessage response)
    { response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<CompatibilityDecisionResultDto>())!; }
    private async Task<ContractPublicationResultDto> PublishPact(string sha)
    {
        using var response = await _client.PostAsJsonAsync("/api/contract-publications", new
        {
            sourceApplicationId = "payments",
            commitSha = sha,
            kind = "ConsumerPact",
            consumerApplicationId = "payments",
            providerApplicationId = "invoice",
            sourceRef = "refs/heads/main",
            buildUrl = PaymentsRepository + "/actions/runs/1",
            contentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(ContractSamples.Pact3))
        });
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<ContractPublicationResultDto>())!;
    }
    private async Task<VerificationRunResultDto> Run(string status, string providerSha = Sha, int attempt = 1, string checkId = "pact/orders", string? change = null, ContractPublicationResultDto? consumer = null)
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
            verifierName = change == "verifier" ? "other-tool" : "pact-core",
            verifierVersion = change == "version" ? "2" : "1",
            configurationSha256 = new string(change == "configuration" ? 'e' : 'd', 64),
            ciRunId = "123",
            ciAttempt = attempt,
            ciCheckId = checkId,
            sourceRef = change == "ref" ? "refs/heads/feature" : "refs/heads/main",
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
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<VerificationRunResultDto>())!;
    }
    private Task<long> Count(string collection) => _database.GetCollection<BsonDocument>(collection).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
    private WebApplicationFactory<Program> Factory() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Production"); builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        { ["Mongo:ConnectionString"] = mongo.ConnectionString, ["Mongo:DatabaseName"] = _databaseName }.Concat(PublisherTestSettings.Configuration)));
    });
    private static HttpClient Client(WebApplicationFactory<Program> factory) { var client = factory.CreateClient(); PublisherTestSettings.Authorize(client); return client; }
    private static async Task Problem(HttpResponseMessage response, HttpStatusCode status)
    { Assert.Equal(status, response.StatusCode); Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType); await response.Content.ReadAsStringAsync(); }
}
