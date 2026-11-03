using ECommerceStore.Pact.Application.Reporting;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceStore.Pact.Api.Reporting;

public class PageReadRequest
{
    [FromQuery(Name = "limit")] public int? Limit { get; init; }
    [FromQuery(Name = "after")] public string? After { get; init; }
}
public sealed class ApplicationsReadRequest : PageReadRequest
{
    [FromQuery(Name = "search")] public string? Search { get; init; }
}
public class IntegrationSelectionRequest
{
    [FromQuery(Name = "applicationId")] public string? ApplicationId { get; init; }
    [FromQuery(Name = "consumerApplicationId")] public string? ConsumerApplicationId { get; init; }
    [FromQuery(Name = "providerApplicationId")] public string? ProviderApplicationId { get; init; }
    public RegistryReadFilter Filter() => new() { ApplicationId = ApplicationId, ConsumerApplicationId = ConsumerApplicationId, ProviderApplicationId = ProviderApplicationId };
}
public sealed class IntegrationsReadRequest : IntegrationSelectionRequest
{
    [FromQuery(Name = "limit")] public int? Limit { get; init; }
    [FromQuery(Name = "after")] public string? After { get; init; }
}
public class EvidenceSelectionRequest : IntegrationSelectionRequest
{
    [FromQuery(Name = "consumerCommitSha")] public string? ConsumerCommitSha { get; init; }
    [FromQuery(Name = "providerCommitSha")] public string? ProviderCommitSha { get; init; }
    [FromQuery(Name = "contractSha256")] public string? ContractSha256 { get; init; }
    [FromQuery(Name = "sourceRef")] public string? SourceRef { get; init; }
    [FromQuery(Name = "status")] public string? Status { get; init; }
    [FromQuery(Name = "issueCode")] public string? IssueCode { get; init; }
    [FromQuery(Name = "jsonPath")] public string? JsonPath { get; init; }
    [FromQuery(Name = "ciRunId")] public string? CiRunId { get; init; }
    [FromQuery(Name = "ciAttempt")] public int? CiAttempt { get; init; }
    [FromQuery(Name = "ciCheckId")] public string? CiCheckId { get; init; }
    [FromQuery(Name = "verifierName")] public string? VerifierName { get; init; }
    [FromQuery(Name = "verifierVersion")] public string? VerifierVersion { get; init; }
    [FromQuery(Name = "configurationSha256")] public string? ConfigurationSha256 { get; init; }
    public RegistryReadFilter EvidenceFilter() => Filter() with
    {
        ConsumerCommitSha = ConsumerCommitSha,
        ProviderCommitSha = ProviderCommitSha,
        ContractSha256 = ContractSha256,
        SourceRef = SourceRef,
        Status = Status,
        IssueCode = IssueCode,
        JsonPath = JsonPath,
        CiRunId = CiRunId,
        CiAttempt = CiAttempt,
        CiCheckId = CiCheckId,
        VerifierName = VerifierName,
        VerifierVersion = VerifierVersion,
        ConfigurationSha256 = ConfigurationSha256
    };
}
public sealed class EvidenceReadRequest : EvidenceSelectionRequest
{
    [FromQuery(Name = "limit")] public int? Limit { get; init; }
    [FromQuery(Name = "after")] public string? After { get; init; }
}
public sealed class PublicationsReadRequest : PageReadRequest
{
    [FromQuery(Name = "sourceApplicationId")] public string? SourceApplicationId { get; init; }
    [FromQuery(Name = "commitSha")] public string? CommitSha { get; init; }
    [FromQuery(Name = "kind")] public string? Kind { get; init; }
    [FromQuery(Name = "consumerApplicationId")] public string? ConsumerApplicationId { get; init; }
    [FromQuery(Name = "providerApplicationId")] public string? ProviderApplicationId { get; init; }
    [FromQuery(Name = "sourceRef")] public string? SourceRef { get; init; }
    [FromQuery(Name = "contractSha256")] public string? ContractSha256 { get; init; }
    public RegistryReadFilter Filter() => new()
    {
        SourceApplicationId = SourceApplicationId,
        CommitSha = CommitSha,
        Kind = Kind,
        ConsumerApplicationId = ConsumerApplicationId,
        ProviderApplicationId = ProviderApplicationId,
        SourceRef = SourceRef,
        ContractSha256 = ContractSha256
    };
}
public sealed class ProfilesReadRequest : PageReadRequest
{
    [FromQuery(Name = "consumerApplicationId")] public string? ConsumerApplicationId { get; init; }
    [FromQuery(Name = "providerApplicationId")] public string? ProviderApplicationId { get; init; }
    [FromQuery(Name = "name")] public string? Name { get; init; }
    [FromQuery(Name = "version")] public string? Version { get; init; }
    public RegistryReadFilter Filter() => new() { ConsumerApplicationId = ConsumerApplicationId, ProviderApplicationId = ProviderApplicationId, Name = Name, Version = Version };
}
public sealed class DecisionsReadRequest : PageReadRequest
{
    [FromQuery(Name = "consumerApplicationId")] public string? ConsumerApplicationId { get; init; }
    [FromQuery(Name = "providerApplicationId")] public string? ProviderApplicationId { get; init; }
    [FromQuery(Name = "consumerCommitSha")] public string? ConsumerCommitSha { get; init; }
    [FromQuery(Name = "providerCommitSha")] public string? ProviderCommitSha { get; init; }
    [FromQuery(Name = "contractSha256")] public string? ContractSha256 { get; init; }
    [FromQuery(Name = "profileId")] public Guid? ProfileId { get; init; }
    [FromQuery(Name = "status")] public string? Status { get; init; }
    public RegistryReadFilter Filter() => new()
    {
        ConsumerApplicationId = ConsumerApplicationId,
        ProviderApplicationId = ProviderApplicationId,
        ConsumerCommitSha = ConsumerCommitSha,
        ProviderCommitSha = ProviderCommitSha,
        ContractSha256 = ContractSha256,
        ProfileId = ProfileId,
        Status = Status
    };
}
public sealed class ComparisonsReadRequest : PageReadRequest
{
    [FromQuery(Name = "consumerApplicationId")] public string? ConsumerApplicationId { get; init; }
    [FromQuery(Name = "providerApplicationId")] public string? ProviderApplicationId { get; init; }
    [FromQuery(Name = "sourceApplicationId")] public string? SourceApplicationId { get; init; }
    [FromQuery(Name = "kind")] public string? Kind { get; init; }
    [FromQuery(Name = "baselineRef")] public string? BaselineRef { get; init; }
    public RegistryReadFilter Filter() => new()
    {
        ConsumerApplicationId = ConsumerApplicationId,
        ProviderApplicationId = ProviderApplicationId,
        SourceApplicationId = SourceApplicationId,
        Kind = Kind,
        BaselineRef = BaselineRef
    };
}
