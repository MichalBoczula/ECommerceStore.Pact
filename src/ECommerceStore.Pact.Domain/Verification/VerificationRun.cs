using ECommerceStore.Pact.Domain.Contracts;

namespace ECommerceStore.Pact.Domain.Verification;

public sealed record VerificationTarget
{
    public Guid ConsumerVersionId { get; }
    public ApplicationVersionIdentity Consumer { get; }
    public Guid ProviderVersionId { get; }
    public ApplicationVersionIdentity Provider { get; }
    public Guid PublicationId { get; }
    public Guid BindingId { get; }
    public string ContractSha256 { get; }
    public VerificationTarget(Guid consumerVersionId, ApplicationVersionIdentity consumer, Guid providerVersionId,
        ApplicationVersionIdentity provider, Guid publicationId, Guid bindingId, string hash)
    {
        if (new[] { consumerVersionId, providerVersionId, publicationId, bindingId }.Contains(Guid.Empty))
            throw new RegistryValidationException("references", "Nonempty version, publication and binding IDs are required.");
        _ = new IntegrationIdentity(consumer.ApplicationId, provider.ApplicationId);
        ConsumerVersionId = consumerVersionId; Consumer = consumer; ProviderVersionId = providerVersionId;
        Provider = provider; PublicationId = publicationId; BindingId = bindingId;
        ContractSha256 = ContractArtifact.NormalizeHash(hash);
    }
}

public sealed record VerificationExecution
{
    public string RunId { get; }
    public int Attempt { get; }
    public string CheckId { get; }
    public string VerifierName { get; }
    public string VerifierVersion { get; }
    public string ConfigurationSha256 { get; }
    public VerificationExecution(string? runId, int attempt, string? checkId, string? verifierName, string? verifierVersion, string configurationSha256)
    {
        RunId = VerificationValues.Text(runId, "ciRunId", 200);
        if (attempt is < 1 or > 10000) throw new RegistryValidationException("ciAttempt", "Use an attempt from 1 to 10000.");
        Attempt = attempt;
        CheckId = VerificationValues.Text(checkId, "ciCheckId", 200);
        VerifierName = VerificationValues.Text(verifierName, "verifierName", 100);
        VerifierVersion = VerificationValues.Text(verifierVersion, "verifierVersion", 100);
        ConfigurationSha256 = ContractArtifact.NormalizeHash(configurationSha256);
    }
}

public sealed class VerificationRun
{
    public Guid Id { get; }
    public VerificationTarget Target { get; }
    public VerificationExecution Execution { get; }
    public ContractProvenance Provenance { get; }
    public string ProviderRepositoryUrl { get; }
    public VerificationStatus Status { get; }
    public int ExpectedInteractions { get; }
    public int VerifiedInteractions { get; }
    public int FailedInteractions { get; }
    public string Summary { get; }
    public IReadOnlyList<VerificationIssue> Issues { get; }
    public IReadOnlyList<string> EvidenceUrls { get; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset FinishedAt { get; }
    public DateTimeOffset RecordedAt { get; }
    public string ExecutionKey => VerificationValues.Hash("verification-execution-v1", Target.ConsumerVersionId, Target.ProviderVersionId, Target.BindingId,
        Target.ContractSha256, Execution.RunId, Execution.Attempt, Execution.CheckId);
    public string PayloadSha256 => VerificationValues.Hash("verification-report-v1", Target.ConsumerVersionId,
        Target.Consumer.ApplicationId, Target.Consumer.CommitSha, Target.ProviderVersionId, Target.Provider.ApplicationId,
        Target.Provider.CommitSha, Target.PublicationId, Target.BindingId, Target.ContractSha256, Execution.RunId, Execution.Attempt,
        Execution.CheckId, Execution.VerifierName, Execution.VerifierVersion, Execution.ConfigurationSha256, Provenance.SourceRef, Provenance.BuildUrl,
        ProviderRepositoryUrl, Status, ExpectedInteractions, VerifiedInteractions, FailedInteractions, Summary,
        Issues.Select(issue => VerificationValues.Hash(issue.Kind, issue.Code, issue.Message, issue.Interaction, issue.Method, issue.Path,
            issue.JsonPath, issue.ExpectedJson, issue.ActualPresent, issue.ActualJson, issue.EvidenceUrl)).Order(StringComparer.Ordinal).ToArray(),
        EvidenceUrls.Order(StringComparer.Ordinal).ToArray(), StartedAt, FinishedAt);

    private VerificationRun(Guid id, VerificationTarget target, VerificationExecution execution, ContractProvenance provenance,
        string providerRepositoryUrl, VerificationStatus status, int expectedInteractions, int verifiedInteractions, int failedInteractions,
        string? summary, IEnumerable<VerificationIssue> issues, IEnumerable<string> evidenceUrls,
        DateTimeOffset startedAt, DateTimeOffset finishedAt, DateTimeOffset recordedAt)
    {
        if (id == Guid.Empty) throw new RegistryValidationException("id", "A nonempty execution ID is required.");
        var diagnostics = issues.Take(101).ToArray();
        var links = evidenceUrls.Take(11).Select(VerificationValues.EvidenceUrl).ToArray();
        if (diagnostics.Length > 100 || links.Length > 10) throw new RegistryValidationException("issues", "At most 100 issues and 10 evidence URLs are supported.");
        if (!Enum.IsDefined(status) || expectedInteractions < 1 || verifiedInteractions < 0 || failedInteractions < 0 ||
            (long)verifiedInteractions + failedInteractions > expectedInteractions)
            throw new RegistryValidationException("status", "Use a supported status and interaction counts within the referenced Pact.");
        var mismatches = diagnostics.Count(issue => issue.Kind == VerificationIssueKind.Mismatch);
        var errors = diagnostics.Count(issue => issue.Kind == VerificationIssueKind.ExecutionError);
        if ((mismatches > 0) != (failedInteractions > 0) ||
            (status == VerificationStatus.Passed && (verifiedInteractions != expectedInteractions || diagnostics.Length != 0)) ||
            (status == VerificationStatus.Failed && (failedInteractions == 0 || errors != 0)) ||
            (status == VerificationStatus.Error && errors == 0) ||
            (status == VerificationStatus.Unverified && (verifiedInteractions != 0 || failedInteractions != 0 || diagnostics.Length != 0)))
            throw new RegistryValidationException("status", "Status, coverage and diagnostics are inconsistent; incomplete execution cannot pass.");
        if (startedAt == default || finishedAt == default || finishedAt < startedAt)
            throw new RegistryValidationException("finishedAt", "Provide start/end timestamps in order.");
        Id = id; Target = target; Execution = execution;
        // Validate the provider provenance even on rehydration.
        Provenance = new ContractProvenance(provenance.SourceRef, provenance.BuildUrl, provenance.PublisherId, providerRepositoryUrl);
        ProviderRepositoryUrl = providerRepositoryUrl; Status = status; ExpectedInteractions = expectedInteractions;
        VerifiedInteractions = verifiedInteractions; FailedInteractions = failedInteractions;
        Summary = VerificationValues.Text(summary, "summary", 4096, multiline: true);
        Issues = Array.AsReadOnly(diagnostics); EvidenceUrls = Array.AsReadOnly(links);
        StartedAt = RegistryIdentity.Timestamp(startedAt); FinishedAt = RegistryIdentity.Timestamp(finishedAt); RecordedAt = RegistryIdentity.Timestamp(recordedAt);
    }

    public static VerificationRun Create(VerificationTarget target, VerificationExecution execution, ContractProvenance provenance,
        string providerRepositoryUrl, VerificationStatus status, int expectedInteractions, int verifiedInteractions, int failedInteractions,
        string? summary, IEnumerable<VerificationIssue> issues, IEnumerable<string> evidenceUrls, DateTimeOffset startedAt,
        DateTimeOffset finishedAt, DateTimeOffset recordedAt) => new(Guid.NewGuid(), target, execution, provenance, providerRepositoryUrl,
            status, expectedInteractions, verifiedInteractions, failedInteractions, summary, issues, evidenceUrls, startedAt, finishedAt, recordedAt);

    public static VerificationRun Rehydrate(Guid id, VerificationTarget target, VerificationExecution execution, ContractProvenance provenance,
        string providerRepositoryUrl, VerificationStatus status, int expectedInteractions, int verifiedInteractions, int failedInteractions,
        string? summary, IEnumerable<VerificationIssue> issues, IEnumerable<string> evidenceUrls, DateTimeOffset startedAt,
        DateTimeOffset finishedAt, DateTimeOffset recordedAt) => new(id, target, execution, provenance, providerRepositoryUrl,
            status, expectedInteractions, verifiedInteractions, failedInteractions, summary, issues, evidenceUrls, startedAt, finishedAt, recordedAt);
}
