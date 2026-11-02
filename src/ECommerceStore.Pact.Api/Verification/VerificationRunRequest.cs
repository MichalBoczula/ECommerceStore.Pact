using System.Text.Json;
using System.Text.Json.Serialization;
using ECommerceStore.Pact.Application.Verification;
using ECommerceStore.Pact.Domain;
using ECommerceStore.Pact.Domain.Verification;

namespace ECommerceStore.Pact.Api.Verification;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record VerificationRunRequest
{
    public required Guid ConsumerPublicationId { get; init; }
    public required string ConsumerApplicationId { get; init; }
    public required string ConsumerCommitSha { get; init; }
    public required string ProviderApplicationId { get; init; }
    public required string ProviderCommitSha { get; init; }
    public required string ContractSha256 { get; init; }
    public required string VerifierName { get; init; }
    public required string VerifierVersion { get; init; }
    public required string ConfigurationSha256 { get; init; }
    public required string CiRunId { get; init; }
    public required int CiAttempt { get; init; }
    public required string CiCheckId { get; init; }
    public required string SourceRef { get; init; }
    public required string BuildUrl { get; init; }
    public required string Status { get; init; }
    public required int VerifiedInteractions { get; init; }
    public required int FailedInteractions { get; init; }
    public required string Summary { get; init; }
    public required IReadOnlyList<VerificationIssueRequest> Issues { get; init; }
    public required IReadOnlyList<string> EvidenceUrls { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset FinishedAt { get; init; }

    internal VerificationRunCommand ToCommand()
    {
        if (Issues is null || EvidenceUrls is null || Issues.Count > 100 || EvidenceUrls.Count > 10 || Issues.Any(issue => issue is null))
            throw new RegistryValidationException("issues", "Provide arrays with at most 100 nonnull issues and 10 evidence URLs.");
        return new(ConsumerPublicationId, ConsumerApplicationId, ConsumerCommitSha, ProviderApplicationId, ProviderCommitSha, ContractSha256,
            VerifierName, VerifierVersion, ConfigurationSha256, CiRunId, CiAttempt, CiCheckId, SourceRef, BuildUrl, Status,
            VerifiedInteractions, FailedInteractions, Summary, Issues.Select(issue => issue.ToDomain()).ToArray(), EvidenceUrls, StartedAt, FinishedAt);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record VerificationIssueRequest
{
    public required string Kind { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? Interaction { get; init; }
    public string? Method { get; init; }
    public string? Path { get; init; }
    public string? JsonPath { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Expected { get; init; }
    public bool? ActualPresent { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Actual { get; init; }
    public string? EvidenceUrl { get; init; }

    internal VerificationIssue ToDomain() => new(VerificationRunService.Parse<VerificationIssueKind>(Kind, "issues.kind"), Code, Message,
        Interaction, Method, Path, JsonPath, Expected.ValueKind == JsonValueKind.Undefined ? null : Expected.GetRawText(),
        ActualPresent, Actual.ValueKind == JsonValueKind.Undefined ? null : Actual.GetRawText(), EvidenceUrl);
}
