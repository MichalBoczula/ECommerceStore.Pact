using ECommerceStore.Pact.Domain.Verification;

namespace ECommerceStore.Pact.Application.Verification;

public sealed record VerificationRunCommand(Guid ConsumerPublicationId, string ConsumerApplicationId, string ConsumerCommitSha,
    string ProviderApplicationId, string ProviderCommitSha, string ContractSha256, string VerifierName, string VerifierVersion,
    string ConfigurationSha256, string CiRunId, int CiAttempt, string CiCheckId, string SourceRef, string BuildUrl,
    string Status, int VerifiedInteractions, int FailedInteractions, string Summary, IReadOnlyList<VerificationIssue> Issues,
    IReadOnlyList<string> EvidenceUrls, DateTimeOffset StartedAt, DateTimeOffset FinishedAt);

public sealed class VerificationConflictException() : Exception("This CI execution already has a different immutable report.");

public sealed record VerificationPage(IReadOnlyList<VerificationRun> Items, Guid? NextCursor);
