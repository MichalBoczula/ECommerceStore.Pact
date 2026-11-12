using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ECommerceStore.Pact.Api.Compatibility;
using ECommerceStore.Pact.Api.Contracts;
using ECommerceStore.Pact.Api.History;
using ECommerceStore.Pact.Api.Registry;
using ECommerceStore.Pact.Api.Reporting;
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
public sealed class RegistryBrowseTests(MongoFixture mongo) : IAsyncLifetime
{
    private static readonly string Sha = new('a', 40);
    private static readonly string NextSha = new('b', 40);
    private static readonly string ConfigurationHash = new('d', 64);
    private readonly string _databaseName = $"pact-browse-{Guid.NewGuid():N}";
    private readonly TestClock _clock = new();
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _write = null!;
    private HttpClient _read = null!;
    private IMongoDatabase _database = null!;

    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mongo:ConnectionString"] = mongo.ConnectionString,
                ["Mongo:DatabaseName"] = _databaseName
            }.Concat(PublisherTestSettings.Configuration)));
            builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(_clock));
        });
        _write = _factory.CreateClient(); PublisherTestSettings.Authorize(_write);
        _read = _factory.CreateClient(); // All browse endpoints are intentionally anonymous reads.
        _database = _factory.Services.GetRequiredService<IMongoDatabase>();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        try { await _database.Client.DropDatabaseAsync(_databaseName); }
        finally { _write.Dispose(); _read.Dispose(); await _factory.DisposeAsync(); }
    }

    [Fact]
    public async Task EmptyRegistryReturnsEmptyPagesAndZeroCountsWithoutCreatingBusinessRecords()
    {
        foreach (var path in new[] { "/api/applications", "/api/applications/invoice/versions", "/api/integrations", "/api/contract-publications",
            "/api/reports/verification-runs", "/api/verification-profiles", "/api/compatibility-decisions", "/api/contract-comparisons" })
        {
            using var json = JsonDocument.Parse(await _read.GetStringAsync(path));
            Assert.Equal(0, json.RootElement.GetProperty("items").GetArrayLength());
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("nextCursor").ValueKind);
        }
        var graph = await Get<DependencyGraphResultDto>("/api/reports/dependencies");
        Assert.Empty(graph.Nodes); Assert.Empty(graph.Edges); Assert.True(graph.IsComplete);
        var overview = await Get<RegistryOverviewResultDto>("/api/reports/overview");
        Assert.Equal(0, overview.Inventory.RegisteredApplications); Assert.Equal(0, overview.MatchingReports.Total);
        using var names = await _database.ListCollectionNamesAsync();
        foreach (var name in await names.ToListAsync())
            Assert.Equal(0, await _database.GetCollection<BsonDocument>(name).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
    }

    [Fact]
    public async Task ApplicationPrefixPaginationGroupsVersionsAndRejectsCursorReuseWithDifferentFilters()
    {
        await Seed();
        var first = await Get<RegistryPageResultDto<RegisteredApplicationResultDto>>("/api/applications?search=P&limit=1");
        var payments = Assert.Single(first.Items);
        Assert.Equal("payments", payments.ApplicationId); Assert.Equal(2, payments.VersionCount); Assert.NotNull(first.NextCursor);
        var next = await Get<RegistryPageResultDto<RegisteredApplicationResultDto>>("/api/applications?search=p&limit=100&after=" + first.NextCursor);
        Assert.Equal("products", Assert.Single(next.Items).ApplicationId); Assert.Null(next.NextCursor);
        using var differentFilter = await _read.GetAsync("/api/applications?search=i&after=" + first.NextCursor);
        Assert.Equal(HttpStatusCode.BadRequest, differentFilter.StatusCode);
        using var differentResource = await _read.GetAsync("/api/applications/payments/versions?after=" + first.NextCursor);
        Assert.Equal(HttpStatusCode.BadRequest, differentResource.StatusCode);
        Assert.Equal(2, (await Get<RegistryPageResultDto<ApplicationVersionResultDto>>(payments.VersionsUrl)).Items.Count);
    }

    [Fact]
    public async Task VersionPagingHasADeterministicTieBreakerAndDoesNotRepeatRowsWhenANewerVersionArrives()
    {
        await Seed();
        var all = await Get<RegistryPageResultDto<ApplicationVersionResultDto>>("/api/applications/invoice/versions");
        Assert.Equal(all.Items.OrderBy(v => v.Id.ToString("D"), StringComparer.Ordinal).Select(v => v.Id), all.Items.Select(v => v.Id));
        var first = await Get<RegistryPageResultDto<ApplicationVersionResultDto>>("/api/applications/invoice/versions?limit=1");
        _clock.Now = _clock.Now.AddMinutes(1);
        var added = await Version("invoice", new string('c', 40));
        var next = await Get<RegistryPageResultDto<ApplicationVersionResultDto>>("/api/applications/invoice/versions?limit=1&after=" + first.NextCursor);
        Assert.NotEqual(first.Items[0].Id, Assert.Single(next.Items).Id); Assert.Null(next.NextCursor);
        Assert.Equal(all.Items.Select(v => v.Id).ToHashSet(), first.Items.Concat(next.Items).Select(v => v.Id).ToHashSet());
        Assert.Equal(added.Id, (await Get<RegistryPageResultDto<ApplicationVersionResultDto>>("/api/applications/invoice/versions?limit=1")).Items[0].Id);
        using var indexes = await _database.GetCollection<BsonDocument>("application_versions").Indexes.ListAsync();
        var index = Assert.Single(await indexes.ToListAsync(), i => i["name"] == "ix_versions_application_browse");
        Assert.Equal(-1, index["key"]["RegisteredAtUtc"].AsInt32); Assert.Equal(1, index["key"]["_id"].AsInt32);
    }

    [Fact]
    public async Task DirectedDeclarationsAndUnregisteredGraphNodesRemainVisibleWithoutReportEvidence()
    {
        await Seed(); await Edge("web", "bff");
        var incoming = await Get<RegistryPageResultDto<IntegrationResultDto>>("/api/integrations?providerApplicationId=invoice");
        Assert.Equal("payments", Assert.Single(incoming.Items).ConsumerApplicationId);
        Assert.Equal(2, (await Get<RegistryPageResultDto<IntegrationResultDto>>("/api/integrations?applicationId=INVOICE")).Items.Count);
        var page = await Get<DependencyGraphResultDto>("/api/reports/dependencies?limit=1");
        Assert.Single(page.Edges); Assert.NotNull(page.NextCursor); Assert.False(page.IsComplete);
        var graph = await Get<DependencyGraphResultDto>("/api/reports/dependencies?consumerApplicationId=web");
        Assert.Equal(new[] { "bff", "web" }, graph.Nodes.Select(n => n.ApplicationId));
        Assert.Equal("Declared", Assert.Single(graph.Edges).Relationship); Assert.Equal(0, graph.Edges[0].MatchingReports.Total);
        Assert.Empty((await Get<RegistryPageResultDto<ApplicationVersionResultDto>>(graph.Nodes[0].VersionsUrl)).Items);
    }

    [Fact]
    public async Task PublicationBrowsingResolvesBindingMetadataAndOriginalDownloadsWithoutEmbeddingContent()
    {
        await Seed();
        var pact = await Publish(true, Sha, ContractSamples.Pact3);
        await Publish(false, Sha, ContractSamples.OpenApi);
        var page = await Get<RegistryPageResultDto<PublicationSummaryResultDto>>("/api/contract-publications?providerApplicationId=invoice");
        Assert.Equal(2, page.Items.Count);
        var provider = Assert.Single((await Get<RegistryPageResultDto<PublicationSummaryResultDto>>(
            "/api/contract-publications?providerApplicationId=invoice&kind=ProviderOpenApi")).Items);
        Assert.Equal("invoice", provider.SourceApplicationId); Assert.Equal("invoice", provider.ProviderApplicationId); Assert.Null(provider.ConsumerApplicationId);
        var consumer = Assert.Single((await Get<RegistryPageResultDto<PublicationSummaryResultDto>>(
            "/api/contract-publications?consumerApplicationId=payments&contractSha256=" + pact.ArtifactSha256.ToUpperInvariant())).Items);
        Assert.Equal(pact.Id, consumer.Id); Assert.Equal(pact.BindingId, consumer.BindingId);
        Assert.Equal(Encoding.UTF8.GetBytes(ContractSamples.Pact3), await _read.GetByteArrayAsync(consumer.ContentUrl));
        Assert.Equal(pact.Id, (await Get<ContractPublicationResultDto>(consumer.DetailUrl)).Id);
        using var json = JsonDocument.Parse(await _read.GetStringAsync("/api/contract-publications"));
        Assert.All(json.RootElement.GetProperty("items").EnumerateArray(), item => Assert.False(item.TryGetProperty("content", out _)));
    }

    [Fact]
    public async Task GraphAndReportSearchLeadFromAMissingFieldToTheVerifiedFix()
    {
        await Seed(); await Edge("web", "invoice");
        var publication = await Publish(true, Sha, ContractSamples.Pact3);
        var failed = await Run(publication, "Failed", "100");
        await Run(publication, "Failed", "101", crossIssues: true);
        var passed = await Run(publication, "Passed", "102", NextSha);
        await Run(publication, "Error", "103"); await Run(publication, "Unverified", "104");

        var graphUrl = "/api/reports/dependencies?providerApplicationId=invoice&limit=1&issueCode=missing_field&jsonPath=" + Uri.EscapeDataString("$.body.totalAmount");
        var page = await Get<DependencyGraphResultDto>(graphUrl);
        Assert.NotNull(page.NextCursor);
        var next = await Get<DependencyGraphResultDto>(graphUrl + "&after=" + page.NextCursor);
        Assert.Null(next.NextCursor);
        var edges = new[] { Assert.Single(page.Edges), Assert.Single(next.Edges) };
        var affected = Assert.Single(edges, edge => edge.Integration.ConsumerApplicationId == "payments");
        Assert.Equal(new VerificationCountsResultDto(0, 1, 0, 0, 1), affected.MatchingReports);
        Assert.Equal(0, Assert.Single(edges, edge => edge.Integration.ConsumerApplicationId == "web").MatchingReports.Total);
        Assert.Equal("Declared", affected.Relationship);
        Assert.Equal(publication.Id, Assert.Single((await Get<RegistryPageResultDto<PublicationSummaryResultDto>>(affected.PublicationsUrl)).Items).Id);

        var matching = await Get<RegistryPageResultDto<VerificationSummaryResultDto>>(affected.ReportsUrl);
        Assert.Equal(failed.Id, Assert.Single(matching.Items).Id);
        var detail = await Get<VerificationRunResultDto>(matching.Items[0].DetailUrl);
        Assert.Equal("Failed", detail.Status);
        var issue = Assert.Single(detail.Issues);
        Assert.Equal("$.body.totalAmount", issue.JsonPath);
        Assert.Equal(12.34m, issue.Expected.GetDecimal());
        Assert.False(issue.ActualPresent);
        Assert.Equal(publication.ArtifactSha256, detail.ContractSha256);
        Assert.Equal(Sha, detail.ProviderCommitSha);

        var fixedReports = await Get<RegistryPageResultDto<VerificationSummaryResultDto>>(
            $"/api/reports/verification-runs?consumerApplicationId=payments&providerApplicationId=invoice&providerCommitSha={NextSha}&contractSha256={publication.ArtifactSha256}&status=passed&ciRunId=102&ciAttempt=1&ciCheckId=pact%2Forders");
        var candidate = Assert.Single(fixedReports.Items);
        Assert.Equal(passed.Id, candidate.Id);
        var history = await Get<IssueHistoryResultDto>(
            $"/api/issue-history?beforeRunId={detail.Id:D}&afterRunId={candidate.Id:D}&baselineRef=refs%2Fheads%2Fmain");
        Assert.True(history.Comparable);
        Assert.Equal("Failed", history.Before.Status);
        Assert.Equal("Passed", history.After.Status);
        Assert.Equal(NextSha, history.After.ProviderCommitSha);
        var resolution = Assert.Single(history.Issues);
        Assert.Equal("Resolved", resolution.Transition);
        Assert.Equal(issue.JsonPath, resolution.Before!.JsonPath);
        Assert.Null(resolution.After);

        var all = await Get<RegistryPageResultDto<VerificationSummaryResultDto>>("/api/reports/verification-runs?applicationId=invoice");
        Assert.Equal(5, all.Items.Count); Assert.Equal(4, all.Items.Select(r => r.Status).Distinct().Count());
        using var json = JsonDocument.Parse(await _read.GetStringAsync(affected.ReportsUrl));
        Assert.False(json.RootElement.GetProperty("items")[0].TryGetProperty("issues", out _));
        Assert.Equal(5, await _database.GetCollection<BsonDocument>("verification_runs").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
    }

    [Fact]
    public async Task OverviewAndGraphCountSelectedReportsWithoutInventingCompatibilityOrDroppingDeclaredGaps()
    {
        await Seed(); var publication = await Publish(true, Sha, ContractSamples.Pact3);
        await Run(publication, "Failed", "100"); await Run(publication, "Passed", "101", NextSha);
        await Run(publication, "Error", "102"); await Run(publication, "Unverified", "103");
        var overview = await Get<RegistryOverviewResultDto>("/api/reports/overview");
        Assert.Equal(3, overview.Inventory.RegisteredApplications); Assert.Equal(5, overview.Inventory.ApplicationVersions);
        Assert.Equal(new VerificationCountsResultDto(1, 1, 1, 1, 4), overview.MatchingReports);
        var selected = await Get<RegistryOverviewResultDto>("/api/reports/overview?providerCommitSha=" + NextSha);
        Assert.Equal(overview.Inventory, selected.Inventory); Assert.Equal(new VerificationCountsResultDto(1, 0, 0, 0, 1), selected.MatchingReports);
        var graph = await Get<DependencyGraphResultDto>("/api/reports/dependencies?providerCommitSha=" + NextSha + "&contractSha256=" + publication.ArtifactSha256);
        Assert.Equal(2, graph.Edges.Count);
        var payments = Assert.Single(graph.Edges, e => e.Integration.ConsumerApplicationId == "payments");
        Assert.Equal(1, payments.MatchingReports.Passed);
        Assert.Single((await Get<RegistryPageResultDto<VerificationSummaryResultDto>>(payments.ReportsUrl)).Items);
        Assert.Equal(0, Assert.Single(graph.Edges, e => e.Integration.ConsumerApplicationId == "invoice").MatchingReports.Total);
        Assert.Contains("not a compatibility decision", graph.EvidencePolicy);
        Assert.Equal(0, (await Get<RegistryOverviewResultDto>("/api/reports/overview?providerCommitSha=" + new string('f', 40))).MatchingReports.Total);
    }

    [Fact]
    public async Task StoredProfilesDecisionsAndComparisonsAreDiscoverableWithoutMixingMatrixRowsOrRecomputingEvidence()
    {
        await Seed(); var before = await Publish(true, Sha, ContractSamples.Pact3);
        var after = await Publish(true, NextSha, ContractSamples.Pact3.Replace("12.34", "13.34", StringComparison.Ordinal));
        await Run(before, "Failed", "100"); await Run(after, "Passed", "101", NextSha);
        using var profileResponse = await _write.PostAsJsonAsync("/api/verification-profiles", new
        {
            consumerApplicationId = "payments",
            providerApplicationId = "invoice",
            name = "required-pact",
            version = "1",
            checks = new[] { new { checkId = "pact/orders", verifierName = "pact-core", verifierVersion = "1.0.0", configurationSha256 = ConfigurationHash } }
        });
        profileResponse.EnsureSuccessStatusCode(); var profile = (await profileResponse.Content.ReadFromJsonAsync<VerificationProfileResultDto>())!;
        using var decision = await _write.PostAsJsonAsync("/api/compatibility-decisions", new
        {
            sourceApplicationId = "invoice",
            profileId = profile.Id,
            rows = new[]
            {
                new { consumerCommitSha = Sha, providerCommitSha = Sha, contractSha256 = before.ArtifactSha256, ciRunId = "100", ciAttempt = 1, providerSourceRef = "refs/heads/main" },
                new { consumerCommitSha = NextSha, providerCommitSha = NextSha, contractSha256 = after.ArtifactSha256, ciRunId = "101", ciAttempt = 1, providerSourceRef = "refs/heads/main" }
            }
        });
        decision.EnsureSuccessStatusCode();
        var selected = Assert.Single((await Get<RegistryPageResultDto<DecisionSummaryResultDto>>(
            $"/api/compatibility-decisions?profileId={profile.Id:D}&consumerCommitSha={NextSha}&providerCommitSha={NextSha}&contractSha256={after.ArtifactSha256}")).Items);
        Assert.Equal(2, selected.RowCount); Assert.Equal("Failed", selected.Status);
        Assert.False((await Get<CompatibilityDecisionResultDto>(selected.DetailUrl)).Compatible);
        Assert.Equal(profile.Id, (await Get<VerificationProfileResultDto>(selected.ProfileUrl)).Id);
        Assert.Empty((await Get<RegistryPageResultDto<DecisionSummaryResultDto>>(
            $"/api/compatibility-decisions?consumerCommitSha={Sha}&providerCommitSha={NextSha}&contractSha256={before.ArtifactSha256}")).Items);
        Assert.Empty((await Get<RegistryPageResultDto<DecisionSummaryResultDto>>("/api/compatibility-decisions?status=Passed")).Items);
        Assert.Equal(profile.Id, Assert.Single((await Get<RegistryPageResultDto<VerificationProfileResultDto>>("/api/verification-profiles?name=REQUIRED-PACT&version=1")).Items).Id);
        using var comparison = await _write.PostAsJsonAsync("/api/contract-comparisons", new
        {
            sourceApplicationId = "payments",
            consumerApplicationId = "payments",
            providerApplicationId = "invoice",
            beforePublicationId = before.Id,
            afterPublicationId = after.Id,
            baselineRef = "refs/heads/main"
        });
        comparison.EnsureSuccessStatusCode();
        var snapshot = Assert.Single((await Get<RegistryPageResultDto<ComparisonSummaryResultDto>>(
            "/api/contract-comparisons?sourceApplicationId=payments&kind=ConsumerPact&baselineRef=refs%2Fheads%2Fmain")).Items);
        Assert.True(snapshot.ContentChanged); Assert.True(snapshot.JsonChanged); Assert.Equal("NotAssessed", snapshot.SchemaAssessment);
        Assert.Equal(before.Id, snapshot.BeforePublicationId); Assert.Equal(after.Id, snapshot.AfterPublicationId);
        Assert.Empty((await Get<RegistryPageResultDto<ComparisonSummaryResultDto>>("/api/contract-comparisons?sourceApplicationId=invoice")).Items);
        await _database.GetCollection<BsonDocument>("compatibility_decisions").UpdateOneAsync(new BsonDocument("_id", selected.Id.ToString("D")),
            new BsonDocument("$set", new BsonDocument("Rows.0.Checks.0.Status", "Passed")));
        using var corrupt = await _read.GetAsync("/api/compatibility-decisions");
        Assert.Equal(HttpStatusCode.InternalServerError, corrupt.StatusCode);
    }

    private async Task Seed()
    {
        foreach (var app in new[] { "payments", "invoice" }) { await Version(app, Sha); await Version(app, NextSha); }
        await Version("products", Sha); await Edge("payments", "invoice"); await Edge("invoice", "products");
    }
    private async Task<ApplicationVersionResultDto> Version(string app, string sha)
    {
        using var response = await _write.PostAsJsonAsync($"/api/applications/{app}/versions", new { commitSha = sha, repositoryUrl = Repository(app) });
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<ApplicationVersionResultDto>())!;
    }
    private async Task Edge(string consumer, string provider)
    {
        using var response = await _write.PostAsJsonAsync("/api/integrations", new { consumerApplicationId = consumer, providerApplicationId = provider });
        response.EnsureSuccessStatusCode();
    }
    private async Task<ContractPublicationResultDto> Publish(bool consumer, string sha, string json)
    {
        var app = consumer ? "payments" : "invoice";
        using var response = await _write.PostAsJsonAsync("/api/contract-publications", new
        {
            sourceApplicationId = app,
            commitSha = sha,
            kind = consumer ? "ConsumerPact" : "ProviderOpenApi",
            consumerApplicationId = consumer ? "payments" : null,
            providerApplicationId = consumer ? "invoice" : null,
            sourceRef = "refs/heads/main",
            buildUrl = Repository(app) + "/actions/runs/1",
            contentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
        });
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<ContractPublicationResultDto>())!;
    }
    private async Task<VerificationRunResultDto> Run(ContractPublicationResultDto publication, string status, string ciRunId, string? providerSha = null, bool crossIssues = false)
    {
        var issues = status switch
        {
            "Failed" when crossIssues => JsonNode.Parse("""[{"kind":"Mismatch","code":"missing_field","message":"Missing currency","interaction":"read an order","method":"GET","path":"/orders/123","jsonPath":"$.body.currency","expected":"EUR","actualPresent":false},{"kind":"Mismatch","code":"other_code","message":"Missing amount","interaction":"read an order","method":"GET","path":"/orders/123","jsonPath":"$.body.totalAmount","expected":12.34,"actualPresent":false}]"""),
            "Failed" => JsonNode.Parse("""[{"kind":"Mismatch","code":"missing_field","message":"Missing amount","interaction":"read an order","method":"GET","path":"/orders/123","jsonPath":"$.body.totalAmount","expected":12.34,"actualPresent":false}]"""),
            "Error" => JsonNode.Parse("""[{"kind":"ExecutionError","code":"startup_timeout","message":"Verifier could not complete"}]"""),
            _ => new JsonArray()
        };
        using var response = await _write.PostAsJsonAsync("/api/verification-runs", new
        {
            consumerPublicationId = publication.Id,
            consumerApplicationId = "payments",
            consumerCommitSha = publication.CommitSha,
            providerApplicationId = "invoice",
            providerCommitSha = providerSha ?? Sha,
            contractSha256 = publication.ArtifactSha256,
            verifierName = "pact-core",
            verifierVersion = "1.0.0",
            configurationSha256 = ConfigurationHash,
            ciRunId,
            ciAttempt = 1,
            ciCheckId = "pact/orders",
            sourceRef = "refs/heads/main",
            buildUrl = Repository("invoice") + "/actions/runs/" + ciRunId,
            status,
            verifiedInteractions = status == "Passed" ? 1 : 0,
            failedInteractions = status == "Failed" ? 1 : 0,
            summary = status == "Unverified" ? "Verification not attempted" : "Synthetic registry browse fixture",
            issues,
            evidenceUrls = Array.Empty<string>(),
            startedAt = _clock.Now.AddSeconds(-2),
            finishedAt = _clock.Now.AddSeconds(-1)
        });
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<VerificationRunResultDto>())!;
    }
    private async Task<T> Get<T>(string url) => (await _read.GetFromJsonAsync<T>(url))!;
    private static string Repository(string app) => "https://github.com/fixtures/" + app;
    private sealed class TestClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
