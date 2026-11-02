using System.Text.Json.Serialization;
using ECommerceStore.Pact.Api.Verification;
using ECommerceStore.Pact.Application.History;
using ECommerceStore.Pact.Domain.History;

namespace ECommerceStore.Pact.Api.History;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CompareContractsRequest
{
    public required string SourceApplicationId { get; init; }
    public required string ConsumerApplicationId { get; init; }
    public required string ProviderApplicationId { get; init; }
    public required Guid BeforePublicationId { get; init; }
    public required Guid AfterPublicationId { get; init; }
    public required string BaselineRef { get; init; }
    internal CompareContractsCommand ToCommand() => new(SourceApplicationId, ConsumerApplicationId, ProviderApplicationId, BeforePublicationId, AfterPublicationId, BaselineRef);
}
public sealed record ComparisonSourceResultDto(Guid PublicationId, Guid BindingId, Guid SourceVersionId, string ApplicationId,
    string CommitSha, string Kind, string ArtifactSha256, string SourceRef)
{
    internal static ComparisonSourceResultDto FromDomain(ComparisonSource value) => new(value.PublicationId, value.BindingId, value.SourceVersionId,
        value.Version.ApplicationId, value.Version.CommitSha, value.Kind.ToString(), value.ArtifactSha256, value.SourceRef);
}
public sealed record SchemaFindingResultDto(string Code, string Level, string Message, string? Method, string? Path);
public sealed record ContractComparisonResultDto(Guid Id, string Profile, string ConsumerApplicationId, string ProviderApplicationId,
    ComparisonSourceResultDto Before, ComparisonSourceResultDto After, string BaselineRef, string PublisherId, bool ContentChanged,
    bool JsonChanged, string SchemaAssessment, string Tool, string ToolVersion, IReadOnlyList<SchemaFindingResultDto> Findings,
    IReadOnlyList<string> Limitations, string ConsumerImpact, DateTimeOffset ComparedAt)
{
    internal static ContractComparisonResultDto FromDomain(ContractComparison value) => new(value.Id, value.Profile,
        value.Integration.ConsumerApplicationId, value.Integration.ProviderApplicationId, ComparisonSourceResultDto.FromDomain(value.Before),
        ComparisonSourceResultDto.FromDomain(value.After), value.BaselineRef, value.PublisherId, value.ContentChanged, value.JsonChanged,
        value.Schema.Assessment.ToString(), value.Schema.Tool, value.Schema.Version,
        value.Schema.Findings.Select(f => new SchemaFindingResultDto(f.Code, f.Level == 3 ? "Error" : f.Level == 2 ? "Warning" : "Info", f.Message, f.Method, f.Path)).ToArray(),
        value.Schema.Limitations, "NotEvaluated", value.ComparedAt);
}
public sealed record HistoricalIssueResultDto(string Key, string Transition, VerificationIssueResultDto? Before, VerificationIssueResultDto? After);
public sealed record IssueHistoryResultDto(string Policy, string BaselineRef, bool Comparable, string Reason,
    VerificationRunResultDto Before, VerificationRunResultDto After, IReadOnlyList<HistoricalIssueResultDto> Issues)
{
    internal static IssueHistoryResultDto FromReport(IssueHistoryReport value, string baselineRef) => new(IssueHistory.Policy, baselineRef,
        value.History.Comparable, value.History.Reason, VerificationRunResultDto.FromDomain(value.Before), VerificationRunResultDto.FromDomain(value.After),
        value.History.Issues.Select(issue => new HistoricalIssueResultDto(issue.Key, issue.Transition.ToString(),
            issue.Before is null ? null : VerificationIssueResultDto.FromDomain(issue.Before), issue.After is null ? null : VerificationIssueResultDto.FromDomain(issue.After))).ToArray());
}
