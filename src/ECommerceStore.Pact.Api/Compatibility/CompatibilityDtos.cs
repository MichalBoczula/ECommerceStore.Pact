using System.Text.Json.Serialization;
using ECommerceStore.Pact.Application.Compatibility;
using ECommerceStore.Pact.Domain;
using ECommerceStore.Pact.Domain.Compatibility;
using ECommerceStore.Pact.Domain.Verification;

namespace ECommerceStore.Pact.Api.Compatibility;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequiredCheckRequest
{
    public required string CheckId { get; init; }
    public required string VerifierName { get; init; }
    public required string VerifierVersion { get; init; }
    public required string ConfigurationSha256 { get; init; }
    internal RequiredVerificationCheck ToDomain() => new(CheckId, VerifierName, VerifierVersion, ConfigurationSha256);
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegisterVerificationProfileRequest
{
    public required string ConsumerApplicationId { get; init; }
    public required string ProviderApplicationId { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required IReadOnlyList<RequiredCheckRequest> Checks { get; init; }
    internal RegisterVerificationProfileCommand ToCommand()
    {
        if (Checks is null || Checks.Count is < 1 or > 10 || Checks.Any(check => check is null))
            throw new RegistryValidationException("checks", "Provide 1 to 10 nonnull required checks.");
        return new(ConsumerApplicationId, ProviderApplicationId, Name, Version, Checks.Select(check => check.ToDomain()).ToArray());
    }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CompatibilityRowRequest
{
    public required string ConsumerCommitSha { get; init; }
    public required string ProviderCommitSha { get; init; }
    public required string ContractSha256 { get; init; }
    public required string CiRunId { get; init; }
    public required int CiAttempt { get; init; }
    public required string ProviderSourceRef { get; init; }
    internal CompatibilityRowCommand ToCommand() => new(ConsumerCommitSha, ProviderCommitSha, ContractSha256, CiRunId, CiAttempt, ProviderSourceRef);
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EvaluateCompatibilityRequest
{
    public required string SourceApplicationId { get; init; }
    public required Guid ProfileId { get; init; }
    public required IReadOnlyList<CompatibilityRowRequest> Rows { get; init; }
    internal EvaluateCompatibilityCommand ToCommand()
    {
        if (Rows is null || Rows.Count is < 1 or > 10 || Rows.Any(row => row is null))
            throw new RegistryValidationException("rows", "Provide 1 to 10 nonnull version/execution selections.");
        return new(SourceApplicationId, ProfileId, Rows.Select(row => row.ToCommand()).ToArray());
    }
}
public sealed record RequiredCheckResultDto(string CheckId, string VerifierName, string VerifierVersion, string ConfigurationSha256)
{
    internal static RequiredCheckResultDto FromDomain(RequiredVerificationCheck value) => new(value.CheckId, value.VerifierName, value.VerifierVersion, value.ConfigurationSha256);
}
public sealed record VerificationProfileResultDto(Guid Id, string ConsumerApplicationId, string ProviderApplicationId, string Name, string Version,
    string ContentSha256, IReadOnlyList<RequiredCheckResultDto> Checks, string PublisherId, DateTimeOffset RegisteredAt)
{
    internal static VerificationProfileResultDto FromDomain(VerificationProfile value) => new(value.Id, value.Integration.ConsumerApplicationId,
        value.Integration.ProviderApplicationId, value.Name, value.Version, value.ContentSha256, value.Checks.Select(RequiredCheckResultDto.FromDomain).ToArray(),
        value.PublisherId, value.RegisteredAt);
}
public sealed record CompatibilityCheckResultDto(RequiredCheckResultDto Required, string Status, string Reason, Guid? EvidenceId, string? EvidenceUrl,
    string? EvidencePayloadSha256, string? ReportedStatus, RequiredCheckResultDto? ObservedVerifier, Guid? ObservedPublicationId, string? BuildUrl, string? PublisherId, int IssueCount)
{
    internal static CompatibilityCheckResultDto FromDomain(CompatibilityCheckResult value) => new(RequiredCheckResultDto.FromDomain(value.Required), value.Status.ToString(), value.Reason.ToString(),
        value.EvidenceId, value.EvidenceId is null ? null : $"/api/verification-runs/{value.EvidenceId}", value.EvidencePayloadSha256, value.ReportedStatus?.ToString(),
        value.ObservedVerifier is null ? null : RequiredCheckResultDto.FromDomain(value.ObservedVerifier), value.ObservedPublicationId, value.BuildUrl, value.PublisherId, value.IssueCount);
}
public sealed record CompatibilityRowResultDto(string ConsumerApplicationId, string ConsumerCommitSha, string ProviderApplicationId, string ProviderCommitSha,
    Guid ConsumerVersionId, Guid ProviderVersionId, Guid? BindingId, string ContractSha256, string CiRunId, int CiAttempt, string ProviderSourceRef,
    string Status, IReadOnlyList<CompatibilityCheckResultDto> Checks, IReadOnlyList<string> MissingChecks, IReadOnlyList<string> UnresolvedChecks)
{
    internal static CompatibilityRowResultDto FromDomain(CompatibilityRow row) => new(row.Selection.Consumer.ApplicationId, row.Selection.Consumer.CommitSha,
        row.Selection.Provider.ApplicationId, row.Selection.Provider.CommitSha, row.Selection.ConsumerVersionId, row.Selection.ProviderVersionId, row.Selection.BindingId,
        row.Selection.ContractSha256, row.Selection.CiRunId, row.Selection.CiAttempt, row.Selection.ProviderSourceRef, row.Status.ToString(), row.Checks.Select(CompatibilityCheckResultDto.FromDomain).ToArray(),
        row.Checks.Where(check => check.Status == VerificationStatus.Unverified).Select(check => check.Required.CheckId).ToArray(),
        row.Checks.Where(check => check.Status != VerificationStatus.Passed).Select(check => check.Required.CheckId).ToArray());
}
public sealed record CompatibilityDecisionResultDto(Guid Id, string Policy, VerificationProfileResultDto Profile, string Status, bool Compatible,
    IReadOnlyList<CompatibilityRowResultDto> Rows, string SourceApplicationId, string PublisherId, DateTimeOffset EvaluatedAt)
{
    internal static CompatibilityDecisionResultDto FromDomain(CompatibilityDecision value) => new(value.Id, value.Policy, VerificationProfileResultDto.FromDomain(value.Profile),
        value.Status.ToString(), value.Compatible, value.Rows.Select(CompatibilityRowResultDto.FromDomain).ToArray(), value.SourceApplicationId, value.PublisherId, value.EvaluatedAt);
}
