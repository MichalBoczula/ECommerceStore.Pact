using ECommerceStore.Pact.Api.Registry;
using ECommerceStore.Pact.Application.Reporting;

namespace ECommerceStore.Pact.Api.Reporting;

public sealed record RegistryPageResultDto<T>(IReadOnlyList<T> Items, string? NextCursor);
public sealed record RegisteredApplicationResultDto(string ApplicationId, long VersionCount, string VersionsUrl);
public sealed record PublicationSummaryResultDto(Guid Id, Guid BindingId, Guid SourceVersionId, string SourceApplicationId, string CommitSha,
    string Kind, string ArtifactSha256, string? ConsumerApplicationId, string ProviderApplicationId, string SourceRef, string BuildUrl,
    string PublisherId, string RepositoryUrl, DateTimeOffset PublishedAt, string DetailUrl, string ContentUrl)
{
    internal static PublicationSummaryResultDto FromRead(PublicationSummary v) => new(v.Id, v.BindingId, v.SourceVersionId, v.SourceApplicationId,
        v.CommitSha, v.Kind, v.ArtifactSha256, v.ConsumerApplicationId, v.ProviderApplicationId, v.SourceRef, v.BuildUrl, v.PublisherId,
        v.RepositoryUrl, v.PublishedAt, $"/api/contract-publications/{v.Id:D}", $"/api/artifacts/{v.ArtifactSha256}/content");
}
public sealed record VerificationSummaryResultDto(Guid Id, string ConsumerApplicationId, string ConsumerCommitSha, string ProviderApplicationId,
    string ProviderCommitSha, Guid ConsumerPublicationId, string ContractSha256, string Status, string Summary, int IssueCount,
    string CiRunId, int CiAttempt, string CiCheckId, string VerifierName, string VerifierVersion, string ConfigurationSha256,
    string SourceRef, string BuildUrl, string PublisherId, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, DateTimeOffset RecordedAt,
    string DetailUrl, string ConsumerPublicationUrl)
{
    internal static VerificationSummaryResultDto FromRead(VerificationSummary v) => new(v.Id, v.ConsumerApplicationId, v.ConsumerCommitSha,
        v.ProviderApplicationId, v.ProviderCommitSha, v.ConsumerPublicationId, v.ContractSha256, v.Status, v.Summary, v.IssueCount,
        v.CiRunId, v.CiAttempt, v.CiCheckId, v.VerifierName, v.VerifierVersion, v.ConfigurationSha256, v.SourceRef, v.BuildUrl, v.PublisherId,
        v.StartedAt, v.FinishedAt, v.RecordedAt, $"/api/verification-runs/{v.Id:D}", $"/api/contract-publications/{v.ConsumerPublicationId:D}");
}
public sealed record DecisionSummaryResultDto(Guid Id, Guid ProfileId, string ProfileName, string ProfileVersion, string Policy,
    string ConsumerApplicationId, string ProviderApplicationId, string Status, int RowCount, string SourceApplicationId,
    string PublisherId, DateTimeOffset EvaluatedAt, string DetailUrl, string ProfileUrl)
{
    internal static DecisionSummaryResultDto FromRead(DecisionSummary v) => new(v.Id, v.ProfileId, v.ProfileName, v.ProfileVersion, v.Policy,
        v.ConsumerApplicationId, v.ProviderApplicationId, v.Status, v.RowCount, v.SourceApplicationId, v.PublisherId, v.EvaluatedAt,
        $"/api/compatibility-decisions/{v.Id:D}", $"/api/verification-profiles/{v.ProfileId:D}");
}
public sealed record ComparisonSummaryResultDto(Guid Id, string ConsumerApplicationId, string ProviderApplicationId, Guid BeforePublicationId,
    Guid AfterPublicationId, string SourceApplicationId, string BeforeCommitSha, string AfterCommitSha, string Kind, string BaselineRef,
    bool ContentChanged, bool JsonChanged, string SchemaAssessment, int FindingCount, string Tool, string ToolVersion, DateTimeOffset ComparedAt,
    string DetailUrl, string BeforePublicationUrl, string AfterPublicationUrl)
{
    internal static ComparisonSummaryResultDto FromRead(ComparisonSummary v) => new(v.Id, v.ConsumerApplicationId, v.ProviderApplicationId,
        v.BeforePublicationId, v.AfterPublicationId, v.SourceApplicationId, v.BeforeCommitSha, v.AfterCommitSha, v.Kind, v.BaselineRef,
        v.ContentChanged, v.JsonChanged, v.SchemaAssessment, v.FindingCount, v.Tool, v.ToolVersion, v.ComparedAt, $"/api/contract-comparisons/{v.Id:D}",
        $"/api/contract-publications/{v.BeforePublicationId:D}", $"/api/contract-publications/{v.AfterPublicationId:D}");
}
public sealed record VerificationCountsResultDto(long Passed, long Failed, long Error, long Unverified, long Total)
{
    internal static VerificationCountsResultDto FromRead(VerificationCounts v) => new(v.Passed, v.Failed, v.Error, v.Unverified, v.Total);
}
public sealed record EvidenceSelectionResultDto(string? ApplicationId, string? ConsumerApplicationId, string? ProviderApplicationId,
    string? ConsumerCommitSha, string? ProviderCommitSha, string? ContractSha256, string? SourceRef, string? Status, string? IssueCode,
    string? JsonPath, string? CiRunId, int? CiAttempt, string? CiCheckId, string? VerifierName, string? VerifierVersion, string? ConfigurationSha256)
{
    internal static EvidenceSelectionResultDto FromRead(RegistryReadFilter f) => new(f.ApplicationId, f.ConsumerApplicationId, f.ProviderApplicationId,
        f.ConsumerCommitSha, f.ProviderCommitSha, f.ContractSha256, f.SourceRef, f.Status, f.IssueCode, f.JsonPath,
        f.CiRunId, f.CiAttempt, f.CiCheckId, f.VerifierName, f.VerifierVersion, f.ConfigurationSha256);
}
public sealed record InventoryCountsResultDto(long RegisteredApplications, long ApplicationVersions, long DeclaredIntegrations, long ContractPublications,
    long VerificationProfiles, long ContractComparisons, long CompatibilityDecisions);
public sealed record RegistryOverviewResultDto(InventoryCountsResultDto Inventory, VerificationCountsResultDto MatchingReports,
    EvidenceSelectionResultDto Selection, string EvidencePolicy, DateTimeOffset ObservedAt);
public sealed record DependencyNodeResultDto(string ApplicationId, string VersionsUrl);
public sealed record DependencyEdgeResultDto(IntegrationResultDto Integration, string Relationship, VerificationCountsResultDto MatchingReports,
    string IntegrationUrl, string ReportsUrl, string PublicationsUrl);
public sealed record DependencyGraphResultDto(IReadOnlyList<DependencyNodeResultDto> Nodes, IReadOnlyList<DependencyEdgeResultDto> Edges,
    string? NextCursor, EvidenceSelectionResultDto Selection, string EvidencePolicy, bool IsComplete);
