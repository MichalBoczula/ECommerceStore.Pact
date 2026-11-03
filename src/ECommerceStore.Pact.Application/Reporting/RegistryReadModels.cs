using ECommerceStore.Pact.Domain;
using ECommerceStore.Pact.Domain.Compatibility;

namespace ECommerceStore.Pact.Application.Reporting;

public sealed record RegisteredApplication(string ApplicationId, long VersionCount);
public sealed record PublicationSummary(Guid Id, Guid BindingId, Guid SourceVersionId, string SourceApplicationId, string CommitSha,
    string Kind, string ArtifactSha256, string? ConsumerApplicationId, string ProviderApplicationId, string SourceRef,
    string BuildUrl, string PublisherId, string RepositoryUrl, DateTimeOffset PublishedAt);
public sealed record VerificationSummary(Guid Id, string ConsumerApplicationId, string ConsumerCommitSha, string ProviderApplicationId,
    string ProviderCommitSha, Guid ConsumerPublicationId, string ContractSha256, string Status, string Summary, int IssueCount,
    string CiRunId, int CiAttempt, string CiCheckId, string VerifierName, string VerifierVersion, string ConfigurationSha256,
    string SourceRef, string BuildUrl, string PublisherId, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, DateTimeOffset RecordedAt);
public sealed record DecisionSummary(Guid Id, Guid ProfileId, string ProfileName, string ProfileVersion, string Policy,
    string ConsumerApplicationId, string ProviderApplicationId, string Status, int RowCount, string SourceApplicationId,
    string PublisherId, DateTimeOffset EvaluatedAt);
public sealed record ComparisonSummary(Guid Id, string ConsumerApplicationId, string ProviderApplicationId, Guid BeforePublicationId,
    Guid AfterPublicationId, string SourceApplicationId, string BeforeCommitSha, string AfterCommitSha, string Kind, string BaselineRef,
    bool ContentChanged, bool JsonChanged, string SchemaAssessment, int FindingCount, string Tool, string ToolVersion, DateTimeOffset ComparedAt);
public sealed record VerificationCounts(long Passed, long Failed, long Error, long Unverified)
{
    public long Total => Passed + Failed + Error + Unverified;
}
public sealed record DependencyEvidence(Integration Integration, VerificationCounts Reports);
public sealed record InventoryCounts(long RegisteredApplications, long ApplicationVersions, long DeclaredIntegrations, long ContractPublications,
    long VerificationProfiles, long ContractComparisons, long CompatibilityDecisions);
public sealed record RegistryOverview(InventoryCounts Inventory, VerificationCounts Reports, RegistryReadFilter Selection, DateTimeOffset ObservedAt);

public interface IRegistryReadRepository
{
    Task<RegistrySlice<RegisteredApplication>> ApplicationsAsync(RegistryReadQuery query, CancellationToken cancellationToken);
    Task<RegistrySlice<ApplicationVersion>> VersionsAsync(RegistryReadQuery query, CancellationToken cancellationToken);
    Task<RegistrySlice<Integration>> IntegrationsAsync(RegistryReadQuery query, CancellationToken cancellationToken);
    Task<RegistrySlice<PublicationSummary>> PublicationsAsync(RegistryReadQuery query, CancellationToken cancellationToken);
    Task<RegistrySlice<VerificationSummary>> RunsAsync(RegistryReadQuery query, CancellationToken cancellationToken);
    Task<RegistrySlice<VerificationProfile>> ProfilesAsync(RegistryReadQuery query, CancellationToken cancellationToken);
    Task<RegistrySlice<DecisionSummary>> DecisionsAsync(RegistryReadQuery query, CancellationToken cancellationToken);
    Task<RegistrySlice<ComparisonSummary>> ComparisonsAsync(RegistryReadQuery query, CancellationToken cancellationToken);
    Task<RegistrySlice<DependencyEvidence>> DependenciesAsync(RegistryReadQuery query, CancellationToken cancellationToken);
    Task<InventoryCounts> InventoryAsync(CancellationToken cancellationToken);
    Task<VerificationCounts> ReportsAsync(RegistryReadFilter filter, CancellationToken cancellationToken);
}
