using MongoDB.Bson.Serialization.Attributes;

namespace ECommerceStore.Pact.Infrastructure.Compatibility;

internal sealed class RequiredCheckDocument
{
    public required string CheckId { get; init; }
    public required string VerifierName { get; init; }
    public required string VerifierVersion { get; init; }
    public required string ConfigurationSha256 { get; init; }
}
internal sealed class VerificationProfileDocument
{
    [BsonId] public required string Id { get; init; }
    public required string Key { get; init; }
    public required string ContentSha256 { get; init; }
    public required string ConsumerApplicationId { get; init; }
    public required string ProviderApplicationId { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required List<RequiredCheckDocument> Checks { get; init; }
    public required string PublisherId { get; init; }
    public DateTime RegisteredAtUtc { get; init; }
}
internal sealed class CompatibilityDecisionDocument
{
    [BsonId] public required string Id { get; init; }
    public required string Key { get; init; }
    public required string Policy { get; init; }
    public required string Status { get; init; }
    public required VerificationProfileDocument Profile { get; init; }
    public required List<CompatibilityRowDocument> Rows { get; init; }
    public required string SourceApplicationId { get; init; }
    public required string PublisherId { get; init; }
    public DateTime EvaluatedAtUtc { get; init; }
}
internal sealed class CompatibilityRowDocument
{
    public required string Status { get; init; }
    public required string ConsumerCommitSha { get; init; }
    public required string ProviderCommitSha { get; init; }
    public required string ConsumerVersionId { get; init; }
    public required string ProviderVersionId { get; init; }
    public string? BindingId { get; init; }
    public required string ContractSha256 { get; init; }
    public required string CiRunId { get; init; }
    public int CiAttempt { get; init; }
    public required string ProviderSourceRef { get; init; }
    public required List<CompatibilityCheckDocument> Checks { get; init; }
}
internal sealed class CompatibilityCheckDocument
{
    public required RequiredCheckDocument Required { get; init; }
    public required string Status { get; init; }
    public required string Reason { get; init; }
    public string? EvidenceId { get; init; }
    public string? EvidencePayloadSha256 { get; init; }
    public string? ReportedStatus { get; init; }
    public RequiredCheckDocument? ObservedVerifier { get; init; }
    public string? ObservedPublicationId { get; init; }
    public string? BuildUrl { get; init; }
    public string? PublisherId { get; init; }
    public int IssueCount { get; init; }
}
