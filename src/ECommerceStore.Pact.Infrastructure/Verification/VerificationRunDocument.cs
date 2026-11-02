using MongoDB.Bson.Serialization.Attributes;

namespace ECommerceStore.Pact.Infrastructure.Verification;

internal sealed class VerificationRunDocument
{
    [BsonId] public required string Id { get; init; }
    public required string ExecutionKey { get; init; }
    public required string PayloadSha256 { get; init; }
    public required string ConsumerVersionId { get; init; }
    public required string ConsumerApplicationId { get; init; }
    public required string ConsumerCommitSha { get; init; }
    public required string ProviderVersionId { get; init; }
    public required string ProviderApplicationId { get; init; }
    public required string ProviderCommitSha { get; init; }
    public required string PublicationId { get; init; }
    public required string BindingId { get; init; }
    public required string ContractSha256 { get; init; }
    public required string CiRunId { get; init; }
    public int CiAttempt { get; init; }
    public required string CiCheckId { get; init; }
    public required string VerifierName { get; init; }
    public required string VerifierVersion { get; init; }
    public required string ConfigurationSha256 { get; init; }
    public required string SourceRef { get; init; }
    public required string BuildUrl { get; init; }
    public required string PublisherId { get; init; }
    public required string ProviderRepositoryUrl { get; init; }
    public required string Status { get; init; }
    public int ExpectedInteractions { get; init; }
    public int VerifiedInteractions { get; init; }
    public int FailedInteractions { get; init; }
    public required string Summary { get; init; }
    public required List<VerificationIssueDocument> Issues { get; init; }
    public required List<string> EvidenceUrls { get; init; }
    public DateTime StartedAtUtc { get; init; }
    public DateTime FinishedAtUtc { get; init; }
    public DateTime RecordedAtUtc { get; init; }
}

internal sealed class VerificationIssueDocument
{
    public required string Kind { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? Interaction { get; init; }
    public string? Method { get; init; }
    public string? Path { get; init; }
    public string? JsonPath { get; init; }
    public string? ExpectedJson { get; init; }
    public bool? ActualPresent { get; init; }
    public string? ActualJson { get; init; }
    public string? EvidenceUrl { get; init; }
}
