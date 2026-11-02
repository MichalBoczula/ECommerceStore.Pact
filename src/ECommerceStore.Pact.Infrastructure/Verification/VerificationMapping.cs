using ECommerceStore.Pact.Domain;
using ECommerceStore.Pact.Domain.Contracts;
using ECommerceStore.Pact.Domain.Verification;

namespace ECommerceStore.Pact.Infrastructure.Verification;

internal static class VerificationMapping
{
    internal static VerificationRunDocument ToDocument(VerificationRun run) => new()
    {
        Id = run.Id.ToString("D"),
        ExecutionKey = run.ExecutionKey,
        PayloadSha256 = run.PayloadSha256,
        ConsumerVersionId = run.Target.ConsumerVersionId.ToString("D"),
        ConsumerApplicationId = run.Target.Consumer.ApplicationId,
        ConsumerCommitSha = run.Target.Consumer.CommitSha,
        ProviderVersionId = run.Target.ProviderVersionId.ToString("D"),
        ProviderApplicationId = run.Target.Provider.ApplicationId,
        ProviderCommitSha = run.Target.Provider.CommitSha,
        PublicationId = run.Target.PublicationId.ToString("D"),
        BindingId = run.Target.BindingId.ToString("D"),
        ContractSha256 = run.Target.ContractSha256,
        CiRunId = run.Execution.RunId,
        CiAttempt = run.Execution.Attempt,
        CiCheckId = run.Execution.CheckId,
        VerifierName = run.Execution.VerifierName,
        VerifierVersion = run.Execution.VerifierVersion,
        ConfigurationSha256 = run.Execution.ConfigurationSha256,
        SourceRef = run.Provenance.SourceRef,
        BuildUrl = run.Provenance.BuildUrl,
        PublisherId = run.Provenance.PublisherId,
        ProviderRepositoryUrl = run.ProviderRepositoryUrl,
        Status = run.Status.ToString(),
        ExpectedInteractions = run.ExpectedInteractions,
        VerifiedInteractions = run.VerifiedInteractions,
        FailedInteractions = run.FailedInteractions,
        Summary = run.Summary,
        Issues = run.Issues.Select(issue => new VerificationIssueDocument
        {
            Kind = issue.Kind.ToString(),
            Code = issue.Code,
            Message = issue.Message,
            Interaction = issue.Interaction,
            Method = issue.Method,
            Path = issue.Path,
            JsonPath = issue.JsonPath,
            ExpectedJson = issue.ExpectedJson,
            ActualPresent = issue.ActualPresent,
            ActualJson = issue.ActualJson,
            EvidenceUrl = issue.EvidenceUrl
        }).ToList(),
        EvidenceUrls = run.EvidenceUrls.ToList(),
        StartedAtUtc = run.StartedAt.UtcDateTime,
        FinishedAtUtc = run.FinishedAt.UtcDateTime,
        RecordedAtUtc = run.RecordedAt.UtcDateTime
    };

    internal static VerificationRun ToDomain(VerificationRunDocument value)
    {
        try
        {
            var target = new VerificationTarget(Guid.Parse(value.ConsumerVersionId), new ApplicationVersionIdentity(value.ConsumerApplicationId, value.ConsumerCommitSha),
                Guid.Parse(value.ProviderVersionId), new ApplicationVersionIdentity(value.ProviderApplicationId, value.ProviderCommitSha),
                Guid.Parse(value.PublicationId), Guid.Parse(value.BindingId), value.ContractSha256);
            var execution = new VerificationExecution(value.CiRunId, value.CiAttempt, value.CiCheckId, value.VerifierName, value.VerifierVersion, value.ConfigurationSha256);
            var provenance = new ContractProvenance(value.SourceRef, value.BuildUrl, value.PublisherId, value.ProviderRepositoryUrl);
            var run = VerificationRun.Rehydrate(Guid.Parse(value.Id), target, execution, provenance, value.ProviderRepositoryUrl,
                Enum.Parse<VerificationStatus>(value.Status), value.ExpectedInteractions, value.VerifiedInteractions, value.FailedInteractions, value.Summary,
                value.Issues.Select(issue => new VerificationIssue(Enum.Parse<VerificationIssueKind>(issue.Kind), issue.Code, issue.Message, issue.Interaction,
                    issue.Method, issue.Path, issue.JsonPath, issue.ExpectedJson, issue.ActualPresent, issue.ActualJson, issue.EvidenceUrl)), value.EvidenceUrls,
                new DateTimeOffset(value.StartedAtUtc, TimeSpan.Zero), new DateTimeOffset(value.FinishedAtUtc, TimeSpan.Zero), new DateTimeOffset(value.RecordedAtUtc, TimeSpan.Zero));
            if (run.ExecutionKey != value.ExecutionKey || run.PayloadSha256 != value.PayloadSha256) throw new InvalidOperationException("Stored verification integrity check failed.");
            return run;
        }
        catch (Exception exception) when (exception is RegistryValidationException or ArgumentException or FormatException)
        { throw new InvalidOperationException("A stored verification record is invalid.", exception); }
    }
}
