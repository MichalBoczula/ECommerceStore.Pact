using System.Text.Json;
using System.Text.Json.Serialization;
using ECommerceStore.Pact.Domain;
using ECommerceStore.Pact.Domain.Contracts;
using ECommerceStore.Pact.Domain.Verification;
using ECommerceStore.Pact.Application.Verification;

namespace ECommerceStore.Pact.Application.Reporting;

public enum RegistryResource { Applications, Versions, Integrations, Publications, Runs, Profiles, Decisions, Comparisons, Dependencies, Overview }

public sealed record RegistryReadFilter
{
    public string? ApplicationId { get; init; }
    public string? SourceApplicationId { get; init; }
    public string? ConsumerApplicationId { get; init; }
    public string? ProviderApplicationId { get; init; }
    public string? CommitSha { get; init; }
    public string? ConsumerCommitSha { get; init; }
    public string? ProviderCommitSha { get; init; }
    public string? ContractSha256 { get; init; }
    public string? SourceRef { get; init; }
    public string? BaselineRef { get; init; }
    public string? Kind { get; init; }
    public string? Status { get; init; }
    public Guid? ProfileId { get; init; }
    public string? Search { get; init; }
    public string? Name { get; init; }
    public string? Version { get; init; }
    public string? IssueCode { get; init; }
    public string? JsonPath { get; init; }
    public string? CiRunId { get; init; }
    public int? CiAttempt { get; init; }
    public string? CiCheckId { get; init; }
    public string? VerifierName { get; init; }
    public string? VerifierVersion { get; init; }
    public string? ConfigurationSha256 { get; init; }

    internal RegistryReadFilter Normalize()
    {
        if (ProfileId == Guid.Empty) throw new RegistryValidationException("profileId", "Select a nonempty profile ID.");
        if (CiAttempt is < 1) throw new RegistryValidationException("ciAttempt", "Select a positive CI attempt.");
        var search = Search?.Trim().ToLowerInvariant();
        if (search is not null && (search.Length is < 1 or > 100 || search.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')))
            throw new RegistryValidationException("search", "Use an application ID prefix of 1 to 100 ASCII letters/digits/hyphens.");
        return this with
        {
            ApplicationId = App(ApplicationId, "applicationId"),
            SourceApplicationId = App(SourceApplicationId, "sourceApplicationId"),
            ConsumerApplicationId = App(ConsumerApplicationId, "consumerApplicationId"),
            ProviderApplicationId = App(ProviderApplicationId, "providerApplicationId"),
            CommitSha = Commit(CommitSha, "commitSha"),
            ConsumerCommitSha = Commit(ConsumerCommitSha, "consumerCommitSha"),
            ProviderCommitSha = Commit(ProviderCommitSha, "providerCommitSha"),
            ContractSha256 = Hash(ContractSha256),
            SourceRef = SourceRef is null ? null : ContractProvenance.NormalizeSourceRef(SourceRef),
            BaselineRef = BaselineRef is null ? null : ContractProvenance.NormalizeSourceRef(BaselineRef),
            Kind = Kind is null ? null : VerificationRunService.Parse<ContractKind>(Kind, "kind").ToString(),
            Status = Status is null ? null : VerificationRunService.Parse<VerificationStatus>(Status, "status").ToString(),
            Search = search,
            Name = App(Name, "name"),
            Version = Text(Version, "version", 100),
            IssueCode = Text(IssueCode, "issueCode", 100),
            JsonPath = Text(JsonPath, "jsonPath", 1024),
            CiRunId = Text(CiRunId, "ciRunId", 200),
            CiCheckId = Text(CiCheckId, "ciCheckId", 200),
            VerifierName = Text(VerifierName, "verifierName", 100),
            VerifierVersion = Text(VerifierVersion, "verifierVersion", 100),
            ConfigurationSha256 = Hash(ConfigurationSha256)
        };
    }

    private static string? App(string? value, string field) => value is null ? null : RegistryIdentity.ApplicationId(value, field);
    private static string? Hash(string? value) => value is null ? null : ContractArtifact.NormalizeHash(value);
    private static string? Text(string? value, string field, int max) => value is null ? null : VerificationValues.Text(value, field, max);
    private static string? Commit(string? value, string field)
    {
        if (value is null) return null;
        try { return new ApplicationVersionIdentity("query", value).CommitSha; }
        catch (RegistryValidationException exception) { throw new RegistryValidationException(field, exception.ValidationMessage); }
    }
}

public sealed record RegistryPosition(string Key, long? Timestamp);

public sealed class RegistryReadQuery
{
    public RegistryResource Resource { get; }
    public RegistryReadFilter Filter { get; }
    public int Limit { get; }
    public RegistryPosition? After { get; }
    private readonly string _scope;

    private RegistryReadQuery(RegistryResource resource, RegistryReadFilter filter, int limit, string? after)
    {
        Resource = resource;
        Filter = filter.Normalize();
        Limit = limit;
        if (limit is < 1 or > 100) throw new RegistryValidationException("limit", "Use a page size from 1 to 100.");
        _scope = VerificationValues.Hash(Resource.ToString(), Filter);
        if (after is not null) After = Decode(after);
    }

    public static RegistryReadQuery Create(RegistryResource resource, RegistryReadFilter filter, int? limit = null, string? after = null) =>
        new(resource, filter, limit ?? 50, after);

    public RegistryPage<T> Page<T>(RegistrySlice<T> slice) => new(slice.Items, slice.Next is null ? null : Encode(slice.Next));

    private string Encode(RegistryPosition position) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(
        new Cursor(1, Resource.ToString(), _scope, position.Key, position.Timestamp))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private RegistryPosition Decode(string value)
    {
        try
        {
            if (value.Length is < 1 or > 2048 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) throw new FormatException();
            var base64 = value.Replace('-', '+').Replace('_', '/');
            var bytes = Convert.FromBase64String(base64.PadRight((base64.Length + 3) / 4 * 4, '='));
            var cursor = JsonSerializer.Deserialize<Cursor>(bytes, new JsonSerializerOptions { MaxDepth = 4 });
            if (cursor is null || cursor.Version != 1 || cursor.Resource != Resource.ToString() || cursor.Scope != _scope) throw new FormatException();
            if (Resource == RegistryResource.Applications)
            {
                if (cursor.Timestamp is not null || RegistryIdentity.ApplicationId(cursor.Key, "after") != cursor.Key) throw new FormatException();
            }
            else
            {
                if (!Guid.TryParseExact(cursor.Key, "D", out var id) || id == Guid.Empty || id.ToString("D") != cursor.Key || cursor.Timestamp is null)
                    throw new FormatException();
                _ = DateTimeOffset.FromUnixTimeMilliseconds(cursor.Timestamp.Value);
            }
            return new RegistryPosition(cursor.Key, cursor.Timestamp);
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException or RegistryValidationException)
        { throw new RegistryValidationException("after", "Use an unchanged cursor from this resource and the same normalized filters."); }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record Cursor(int Version, string Resource, string Scope, string Key, long? Timestamp);
}

public sealed record RegistrySlice<T>(IReadOnlyList<T> Items, RegistryPosition? Next);
public sealed record RegistryPage<T>(IReadOnlyList<T> Items, string? NextCursor);
