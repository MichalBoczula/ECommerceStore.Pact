using System.Text.RegularExpressions;
using ECommerceStore.Pact.Application.Reporting;
using ECommerceStore.Pact.Domain;
using ECommerceStore.Pact.Domain.Compatibility;
using ECommerceStore.Pact.Infrastructure.Compatibility;
using ECommerceStore.Pact.Infrastructure.Registry;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace ECommerceStore.Pact.Infrastructure.Reporting;

internal sealed class MongoRegistryReadRepository(IMongoDatabase database, RegistryIndexes indexes) : IRegistryReadRepository
{
    private static readonly AggregateOptions Options = new() { MaxTime = TimeSpan.FromSeconds(10), AllowDiskUse = false };

    public Task<RegistrySlice<RegisteredApplication>> ApplicationsAsync(RegistryReadQuery query, CancellationToken cancellationToken)
    {
        var conditions = new List<BsonDocument>();
        if (query.Filter.Search is not null) conditions.Add(new("ApplicationId", new BsonRegularExpression("^" + Regex.Escape(query.Filter.Search))));
        if (query.After is not null) conditions.Add(new("ApplicationId", new BsonDocument("$gt", query.After.Key)));
        BsonDocument[] pipeline = [
            new("$match", And(conditions)),
            new("$group", new BsonDocument { ["_id"] = "$ApplicationId", ["VersionCount"] = new BsonDocument("$sum", 1) }),
            new("$sort", new BsonDocument("_id", 1)), new("$limit", query.Limit + 1)
        ];
        return PageAsync("application_versions", pipeline, query, null,
            d => new RegisteredApplication(S(d, "_id"), d["VersionCount"].ToInt64()), cancellationToken);
    }

    public Task<RegistrySlice<ApplicationVersion>> VersionsAsync(RegistryReadQuery query, CancellationToken cancellationToken) =>
        PageAsync("application_versions", Page(Match(("ApplicationId", query.Filter.ApplicationId)), "RegisteredAtUtc", query), query, "RegisteredAtUtc",
            d => RegistryMapping.ToDomain(BsonSerializer.Deserialize<ApplicationVersionDocument>(d)), cancellationToken);

    public Task<RegistrySlice<Integration>> IntegrationsAsync(RegistryReadQuery query, CancellationToken cancellationToken) =>
        PageAsync("integrations", Page(IntegrationFilter(query.Filter), "RegisteredAtUtc", query), query, "RegisteredAtUtc", ReadIntegration, cancellationToken);

    public Task<RegistrySlice<PublicationSummary>> PublicationsAsync(RegistryReadQuery query, CancellationToken cancellationToken)
    {
        var f = query.Filter;
        var binding = new List<BsonDocument>();
        Add(binding, "Binding.SourceApplicationId", f.SourceApplicationId); Add(binding, "Binding.CommitSha", f.CommitSha);
        Add(binding, "Binding.Kind", f.Kind); Add(binding, "Binding.ArtifactSha256", f.ContractSha256);
        Add(binding, "Binding.ConsumerApplicationId", f.ConsumerApplicationId);
        if (f.ProviderApplicationId is not null) binding.Add(new("$or", new BsonArray
        {
            new BsonDocument("Binding.ProviderApplicationId", f.ProviderApplicationId),
            new BsonDocument { ["Binding.Kind"] = "ProviderOpenApi", ["Binding.SourceApplicationId"] = f.ProviderApplicationId }
        }));
        var pipeline = new List<BsonDocument>
        {
            new("$match", Match(("SourceRef", f.SourceRef))),
            new("$lookup", new BsonDocument { ["from"] = "contract_versions", ["localField"] = "BindingId", ["foreignField"] = "_id", ["as"] = "Binding" }),
            new("$unwind", "$Binding"), new("$match", And(binding))
        };
        pipeline.AddRange(Page(new BsonDocument(), "PublishedAtUtc", query));
        return PageAsync("contract_publications", pipeline, query, "PublishedAtUtc", d =>
        {
            var b = d["Binding"].AsBsonDocument;
            return new PublicationSummary(Id(d), Id(b), Guid.Parse(S(b, "SourceVersionId")), S(b, "SourceApplicationId"), S(b, "CommitSha"),
                S(b, "Kind"), S(b, "ArtifactSha256"), Optional(b, "ConsumerApplicationId"), Optional(b, "ProviderApplicationId") ?? S(b, "SourceApplicationId"),
                S(d, "SourceRef"), S(d, "BuildUrl"), S(d, "PublisherId"), S(d, "RepositoryUrl"), Time(d, "PublishedAtUtc"));
        }, cancellationToken);
    }

    public Task<RegistrySlice<VerificationSummary>> RunsAsync(RegistryReadQuery query, CancellationToken cancellationToken)
    {
        var pipeline = Page(RunFilter(query.Filter), "RecordedAtUtc", query);
        pipeline.Add(new("$addFields", new BsonDocument("IssueCount", new BsonDocument("$size", "$Issues"))));
        pipeline.Add(new("$project", new BsonDocument { ["Issues"] = 0, ["EvidenceUrls"] = 0 }));
        return PageAsync("verification_runs", pipeline, query, "RecordedAtUtc", Run, cancellationToken);
    }

    public Task<RegistrySlice<VerificationProfile>> ProfilesAsync(RegistryReadQuery query, CancellationToken cancellationToken) =>
        PageAsync("verification_profiles", Page(Match(("ConsumerApplicationId", query.Filter.ConsumerApplicationId),
            ("ProviderApplicationId", query.Filter.ProviderApplicationId), ("Name", query.Filter.Name), ("Version", query.Filter.Version)), "RegisteredAtUtc", query),
            query, "RegisteredAtUtc", d => CompatibilityMapping.ToDomain(BsonSerializer.Deserialize<VerificationProfileDocument>(d)), cancellationToken);

    public Task<RegistrySlice<DecisionSummary>> DecisionsAsync(RegistryReadQuery query, CancellationToken cancellationToken)
    {
        var f = query.Filter;
        var conditions = new List<BsonDocument> { Match(("Profile.ConsumerApplicationId", f.ConsumerApplicationId),
            ("Profile.ProviderApplicationId", f.ProviderApplicationId), ("Profile._id", f.ProfileId?.ToString("D")), ("Status", f.Status)) };
        var row = Match(("ConsumerCommitSha", f.ConsumerCommitSha), ("ProviderCommitSha", f.ProviderCommitSha), ("ContractSha256", f.ContractSha256));
        // All selected row fields must match the same matrix row.
        if (row.ElementCount > 0) conditions.Add(new("Rows", new BsonDocument("$elemMatch", row)));
        var pipeline = Page(And(conditions), "EvaluatedAtUtc", query);
        return PageAsync("compatibility_decisions", pipeline, query, "EvaluatedAtUtc", d =>
        {
            // Preserve the same immutable-snapshot integrity checks as the exact-ID read.
            // The response is a summary, but it must never surface a corrupt matrix as Passed.
            var value = CompatibilityMapping.ToDomain(BsonSerializer.Deserialize<CompatibilityDecisionDocument>(d));
            return new DecisionSummary(value.Id, value.Profile.Id, value.Profile.Name, value.Profile.Version, value.Policy,
                value.Profile.Integration.ConsumerApplicationId, value.Profile.Integration.ProviderApplicationId, value.Status.ToString(),
                value.Rows.Count, value.SourceApplicationId, value.PublisherId, value.EvaluatedAt);
        }, cancellationToken);
    }

    public Task<RegistrySlice<ComparisonSummary>> ComparisonsAsync(RegistryReadQuery query, CancellationToken cancellationToken)
    {
        var f = query.Filter;
        var pipeline = Page(Match(("ConsumerApplicationId", f.ConsumerApplicationId), ("ProviderApplicationId", f.ProviderApplicationId),
            ("Before.ApplicationId", f.SourceApplicationId), ("Before.Kind", f.Kind), ("BaselineRef", f.BaselineRef)), "ComparedAtUtc", query);
        pipeline.Add(new("$addFields", new BsonDocument("FindingCount", new BsonDocument("$size", "$Findings"))));
        pipeline.Add(new("$project", new BsonDocument { ["Findings"] = 0, ["Limitations"] = 0 }));
        return PageAsync("contract_comparisons", pipeline, query, "ComparedAtUtc", d =>
        {
            var before = d["Before"].AsBsonDocument; var after = d["After"].AsBsonDocument;
            return new ComparisonSummary(Id(d), S(d, "ConsumerApplicationId"), S(d, "ProviderApplicationId"), Guid.Parse(S(before, "PublicationId")),
                Guid.Parse(S(after, "PublicationId")), S(before, "ApplicationId"), S(before, "CommitSha"), S(after, "CommitSha"), S(before, "Kind"),
                S(d, "BaselineRef"), S(before, "ArtifactSha256") != S(after, "ArtifactSha256"), d["JsonChanged"].AsBoolean, S(d, "SchemaAssessment"),
                d["FindingCount"].ToInt32(), S(d, "Tool"), S(d, "ToolVersion"), Time(d, "ComparedAtUtc"));
        }, cancellationToken);
    }

    public async Task<RegistrySlice<DependencyEvidence>> DependenciesAsync(RegistryReadQuery query, CancellationToken cancellationToken)
    {
        var page = await IntegrationsAsync(query, cancellationToken);
        if (page.Items.Count == 0) return new([], page.Next);

        // Count reports only for the directed pairs returned on this page.
        var pairs = new BsonArray(page.Items.Select(edge => Match(
            ("ConsumerApplicationId", edge.Identity.ConsumerApplicationId),
            ("ProviderApplicationId", edge.Identity.ProviderApplicationId))));
        var report = RunFilter(query.Filter with { ApplicationId = null, ConsumerApplicationId = null, ProviderApplicationId = null });
        BsonDocument[] pipeline =
        [
            new("$match", And([report, new BsonDocument("$or", pairs)])),
            CountStatuses(new BsonDocument
            {
                ["Consumer"] = "$ConsumerApplicationId",
                ["Provider"] = "$ProviderApplicationId",
                ["Status"] = "$Status"
            })
        ];
        var groups = await Read(() => Aggregate("verification_runs", pipeline, cancellationToken), cancellationToken);
        var counts = groups.GroupBy(row =>
        {
            var key = row["_id"].AsBsonDocument;
            return new IntegrationIdentity(S(key, "Consumer"), S(key, "Provider"));
        }).ToDictionary(pair => pair.Key, pair => Counts(pair, row => S(row["_id"].AsBsonDocument, "Status")));

        var noReports = new VerificationCounts(0, 0, 0, 0);
        return new(page.Items.Select(edge => new DependencyEvidence(edge, counts.GetValueOrDefault(edge.Identity, noReports))).ToArray(), page.Next);
    }

    public Task<InventoryCounts> InventoryAsync(CancellationToken cancellationToken) => Read(async () =>
    {
        var applications = await Aggregate("application_versions", [new("$group", new BsonDocument("_id", "$ApplicationId")), new("$count", "Count")], cancellationToken);
        return new InventoryCounts(applications.Count == 0 ? 0 : applications[0]["Count"].ToInt64(),
            await Count("application_versions", cancellationToken), await Count("integrations", cancellationToken),
            await Count("contract_publications", cancellationToken), await Count("verification_profiles", cancellationToken),
            await Count("contract_comparisons", cancellationToken), await Count("compatibility_decisions", cancellationToken));
    }, cancellationToken);

    public Task<VerificationCounts> ReportsAsync(RegistryReadFilter filter, CancellationToken cancellationToken) => Read(async () =>
        Counts(await Aggregate("verification_runs", [new("$match", RunFilter(filter)), CountStatuses("$Status")], cancellationToken),
            row => S(row, "_id")), cancellationToken);

    private Task<RegistrySlice<T>> PageAsync<T>(string collection, IEnumerable<BsonDocument> pipeline, RegistryReadQuery query, string? time,
        Func<BsonDocument, T> map, CancellationToken cancellationToken) => Read(async () =>
    {
        var values = await Aggregate(collection, pipeline, cancellationToken);
        var selected = values.Take(query.Limit).ToArray();
        var next = values.Count > query.Limit
            ? new RegistryPosition(S(selected[^1], "_id"), time is null ? null : Time(selected[^1], time).ToUnixTimeMilliseconds()) : null;
        return new RegistrySlice<T>(selected.Select(map).ToArray(), next);
    }, cancellationToken);

    private Task<T> Read<T>(Func<Task<T>> read, CancellationToken cancellationToken) => MongoRegistryOperation.ExecuteAsync(async () =>
    {
        await indexes.EnsureCreatedAsync(cancellationToken);
        return await read();
    });

    private Task<List<BsonDocument>> Aggregate(string collection, IEnumerable<BsonDocument> pipeline, CancellationToken cancellationToken) =>
        database.GetCollection<BsonDocument>(collection).Aggregate<BsonDocument>(pipeline.ToArray(), Options).ToListAsync(cancellationToken);

    private Task<long> Count(string collection, CancellationToken cancellationToken) => database.GetCollection<BsonDocument>(collection)
        .CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty, new CountOptions { MaxTime = Options.MaxTime }, cancellationToken);

    private static List<BsonDocument> Page(BsonDocument filter, string time, RegistryReadQuery query)
    {
        var conditions = new List<BsonDocument> { filter };
        if (query.After is not null)
        {
            var date = new BsonDateTime(DateTimeOffset.FromUnixTimeMilliseconds(query.After.Timestamp!.Value).UtcDateTime);
            conditions.Add(new("$or", new BsonArray
            {
                new BsonDocument(time, new BsonDocument("$lt", date)),
                new BsonDocument { [time] = date, ["_id"] = new BsonDocument("$gt", query.After.Key) }
            }));
        }
        return [new("$match", And(conditions)), new("$sort", new BsonDocument { [time] = -1, ["_id"] = 1 }), new("$limit", query.Limit + 1)];
    }

    private static BsonDocument IntegrationFilter(RegistryReadFilter f)
    {
        var conditions = new List<BsonDocument> { Match(("ConsumerApplicationId", f.ConsumerApplicationId), ("ProviderApplicationId", f.ProviderApplicationId)) };
        if (f.ApplicationId is not null) conditions.Add(new("$or", new BsonArray
        {
            new BsonDocument("ConsumerApplicationId", f.ApplicationId), new BsonDocument("ProviderApplicationId", f.ApplicationId)
        }));
        return And(conditions);
    }

    private static BsonDocument RunFilter(RegistryReadFilter f)
    {
        var conditions = new List<BsonDocument> { Match(("ConsumerApplicationId", f.ConsumerApplicationId), ("ProviderApplicationId", f.ProviderApplicationId),
            ("ConsumerCommitSha", f.ConsumerCommitSha), ("ProviderCommitSha", f.ProviderCommitSha), ("ContractSha256", f.ContractSha256),
            ("SourceRef", f.SourceRef), ("Status", f.Status), ("CiRunId", f.CiRunId), ("CiCheckId", f.CiCheckId),
            ("VerifierName", f.VerifierName), ("VerifierVersion", f.VerifierVersion), ("ConfigurationSha256", f.ConfigurationSha256)) };
        if (f.ApplicationId is not null) conditions.Add(new("$or", new BsonArray
        {
            new BsonDocument("ConsumerApplicationId", f.ApplicationId), new BsonDocument("ProviderApplicationId", f.ApplicationId)
        }));
        if (f.CiAttempt is not null) conditions.Add(new("CiAttempt", f.CiAttempt.Value));
        var issue = Match(("Code", f.IssueCode), ("JsonPath", f.JsonPath));
        // A code and JSON path on different issues must not produce a false match.
        if (issue.ElementCount > 0) conditions.Add(new("Issues", new BsonDocument("$elemMatch", issue)));
        return And(conditions);
    }

    private static VerificationSummary Run(BsonDocument d) => new(Id(d), S(d, "ConsumerApplicationId"), S(d, "ConsumerCommitSha"),
        S(d, "ProviderApplicationId"), S(d, "ProviderCommitSha"), Guid.Parse(S(d, "PublicationId")), S(d, "ContractSha256"), S(d, "Status"),
        S(d, "Summary"), d["IssueCount"].ToInt32(), S(d, "CiRunId"), d["CiAttempt"].ToInt32(), S(d, "CiCheckId"), S(d, "VerifierName"),
        S(d, "VerifierVersion"), S(d, "ConfigurationSha256"), S(d, "SourceRef"), S(d, "BuildUrl"), S(d, "PublisherId"),
        Time(d, "StartedAtUtc"), Time(d, "FinishedAtUtc"), Time(d, "RecordedAtUtc"));

    private static Integration ReadIntegration(BsonDocument d) => Integration.Rehydrate(Id(d),
        new IntegrationIdentity(S(d, "ConsumerApplicationId"), S(d, "ProviderApplicationId")), Time(d, "RegisteredAtUtc"));
    private static BsonDocument CountStatuses(BsonValue key) => new("$group", new BsonDocument { ["_id"] = key, ["Count"] = new BsonDocument("$sum", 1) });
    private static VerificationCounts Counts(IEnumerable<BsonDocument> documents, Func<BsonDocument, string> status)
    {
        var counts = documents.ToDictionary(status, d => d["Count"].ToInt64(), StringComparer.Ordinal);
        if (counts.Keys.Any(key => key is not ("Passed" or "Failed" or "Error" or "Unverified")))
            throw new InvalidOperationException("Stored verification status is invalid.");
        return new(counts.GetValueOrDefault("Passed"), counts.GetValueOrDefault("Failed"), counts.GetValueOrDefault("Error"), counts.GetValueOrDefault("Unverified"));
    }
    private static BsonDocument Match(params (string Field, string? Value)[] fields) => new(fields.Where(f => f.Value is not null).Select(f => new BsonElement(f.Field, f.Value!)));
    private static void Add(List<BsonDocument> list, string field, string? value) { if (value is not null) list.Add(new(field, value)); }
    private static BsonDocument And(IEnumerable<BsonDocument> documents)
    {
        var selected = documents.Where(d => d.ElementCount > 0).ToArray();
        return selected.Length switch { 0 => new BsonDocument(), 1 => selected[0], _ => new BsonDocument("$and", new BsonArray(selected)) };
    }
    private static string S(BsonDocument d, string key) => d[key].AsString;
    private static string? Optional(BsonDocument d, string key) => d.TryGetValue(key, out var value) && !value.IsBsonNull ? value.AsString : null;
    private static Guid Id(BsonDocument d) => Guid.Parse(S(d, "_id"));
    private static DateTimeOffset Time(BsonDocument d, string key) => new(d[key].ToUniversalTime(), TimeSpan.Zero);
}
