using System.Text.Json;
using System.Text.Json.Serialization;
using ECommerceStore.Pact.Domain.Verification;

namespace ECommerceStore.Pact.Api.Verification;

public sealed record VerificationRunResultDto(Guid Id, Guid ConsumerPublicationId, Guid BindingId, Guid ConsumerVersionId,
    string ConsumerApplicationId, string ConsumerCommitSha, Guid ProviderVersionId, string ProviderApplicationId, string ProviderCommitSha,
    string ContractSha256, string VerifierName, string VerifierVersion, string ConfigurationSha256, string CiRunId, int CiAttempt,
    string CiCheckId, string SourceRef, string BuildUrl, string PublisherId, string Status, int ExpectedInteractions,
    int VerifiedInteractions, int FailedInteractions, string Summary, IReadOnlyList<VerificationIssueResultDto> Issues,
    IReadOnlyList<string> EvidenceUrls, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, DateTimeOffset RecordedAt)
{
    internal static VerificationRunResultDto FromDomain(VerificationRun run) => new(run.Id, run.Target.PublicationId, run.Target.BindingId,
        run.Target.ConsumerVersionId, run.Target.Consumer.ApplicationId, run.Target.Consumer.CommitSha,
        run.Target.ProviderVersionId, run.Target.Provider.ApplicationId, run.Target.Provider.CommitSha, run.Target.ContractSha256,
        run.Execution.VerifierName, run.Execution.VerifierVersion, run.Execution.ConfigurationSha256, run.Execution.RunId,
        run.Execution.Attempt, run.Execution.CheckId, run.Provenance.SourceRef, run.Provenance.BuildUrl, run.Provenance.PublisherId,
        run.Status.ToString(), run.ExpectedInteractions, run.VerifiedInteractions, run.FailedInteractions, run.Summary,
        run.Issues.Select(VerificationIssueResultDto.FromDomain).ToArray(), run.EvidenceUrls, run.StartedAt, run.FinishedAt, run.RecordedAt);
}

public sealed record VerificationIssueResultDto(string Kind, string Code, string Message, string? Interaction, string? Method,
    string? Path, string? JsonPath, bool? ActualPresent, string? EvidenceUrl)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Expected { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Actual { get; init; }
    internal static VerificationIssueResultDto FromDomain(VerificationIssue issue) => new(issue.Kind.ToString(), issue.Code, issue.Message,
        issue.Interaction, issue.Method, issue.Path, issue.JsonPath, issue.ActualPresent, issue.EvidenceUrl)
    {
        Expected = Element(issue.ExpectedJson),
        Actual = Element(issue.ActualJson)
    };
    private static JsonElement Element(string? json)
    {
        if (json is null) return default;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

public sealed record VerificationPageResultDto(IReadOnlyList<VerificationRunResultDto> Items, Guid? NextCursor);
