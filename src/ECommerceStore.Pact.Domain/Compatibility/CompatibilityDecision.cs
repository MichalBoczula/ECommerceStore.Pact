using ECommerceStore.Pact.Domain.Verification;

namespace ECommerceStore.Pact.Domain.Compatibility;

public enum CompatibilityReason { ContractNotPublished, MissingExecution, VerifierMismatch, SourceRefMismatch, Reported }

public sealed record CompatibilityCheckResult(RequiredVerificationCheck Required, VerificationStatus Status, CompatibilityReason Reason,
    Guid? EvidenceId, string? EvidencePayloadSha256, VerificationStatus? ReportedStatus, RequiredVerificationCheck? ObservedVerifier,
    Guid? ObservedPublicationId, string? BuildUrl, string? PublisherId, int IssueCount)
{
    public string Fingerprint => VerificationValues.Hash(Required.Fingerprint, Status, Reason, EvidenceId, EvidencePayloadSha256,
        ReportedStatus, ObservedVerifier?.Fingerprint, ObservedPublicationId, BuildUrl, PublisherId, IssueCount);
}
public sealed class CompatibilityRow
{
    public CompatibilitySelection Selection { get; }
    public IReadOnlyList<CompatibilityCheckResult> Checks { get; }
    public VerificationStatus Status { get; }
    public string Fingerprint => VerificationValues.Hash(Selection.StateKey, Status, Checks.Select(check => check.Fingerprint).ToArray());
    public CompatibilityRow(CompatibilitySelection selection, IEnumerable<CompatibilityCheckResult> checks, VerificationStatus? storedStatus = null)
    {
        var values = checks.Take(11).ToArray();
        if (values.Length is < 1 or > 10 || values.Select(value => value.Required.CheckId).Distinct(StringComparer.Ordinal).Count() != values.Length ||
            values.Any(value => !Enum.IsDefined(value.Status) || !Enum.IsDefined(value.Reason) || value.IssueCount is < 0 or > 100 ||
                (value.EvidenceId is null ? value.ReportedStatus is not null || value.ObservedVerifier is not null || value.EvidencePayloadSha256 is not null :
                    value.EvidenceId == Guid.Empty || value.ReportedStatus is null || value.ObservedVerifier is null || value.EvidencePayloadSha256 is null) ||
                (value.Reason != CompatibilityReason.Reported && value.Status != VerificationStatus.Unverified) ||
                (value.Reason == CompatibilityReason.Reported && (value.EvidenceId is null || value.Status != value.ReportedStatus))))
            throw new RegistryValidationException("decision", "Invalid or inconsistent compatibility evidence.");
        Status = storedStatus ?? CompatibilityDecision.Aggregate(values.Select(check => check.Status));
        if (!Enum.IsDefined(Status)) throw new RegistryValidationException("status", "Invalid stored row outcome.");
        Selection = selection; Checks = Array.AsReadOnly(values.OrderBy(check => check.Required.CheckId, StringComparer.Ordinal).ToArray());
    }
}

public sealed class CompatibilityDecision
{
    public const string CurrentPolicy = "explicit-ci-execution-v1";
    public Guid Id { get; }
    public string Policy { get; }
    public VerificationProfile Profile { get; }
    public IReadOnlyList<CompatibilityRow> Rows { get; }
    public string SourceApplicationId { get; }
    public string PublisherId { get; }
    public DateTimeOffset EvaluatedAt { get; }
    public VerificationStatus Status { get; }
    public bool Compatible => Status == VerificationStatus.Passed;
    public string Key => VerificationValues.Hash("compatibility-decision-v1", Policy, Profile.Id, Profile.ContentSha256, Status, Rows.Select(row => row.Fingerprint).ToArray());

    private CompatibilityDecision(Guid id, string policy, VerificationProfile profile, IEnumerable<CompatibilityRow> rows,
        string sourceApplicationId, string publisherId, DateTimeOffset now, VerificationStatus? storedStatus = null)
    {
        var values = rows.Take(11).ToArray();
        var source = RegistryIdentity.ApplicationId(sourceApplicationId, "sourceApplicationId");
        if (id == Guid.Empty || values.Length is < 1 or > 10 || values.Select(row => row.Selection.InputKey).Distinct(StringComparer.Ordinal).Count() != values.Length ||
            (source != profile.Integration.ConsumerApplicationId && source != profile.Integration.ProviderApplicationId) ||
            values.Any(row => row.Selection.Consumer.ApplicationId != profile.Integration.ConsumerApplicationId ||
                row.Selection.Provider.ApplicationId != profile.Integration.ProviderApplicationId ||
                !row.Checks.Select(check => check.Required.Fingerprint).SequenceEqual(profile.Checks.Select(check => check.Fingerprint))))
            throw new RegistryValidationException("rows", "Select 1 to 10 distinct rows for this integration and the complete required check set.");
        Status = storedStatus ?? Aggregate(values.Select(row => row.Status));
        if (!Enum.IsDefined(Status) || (policy == "explicit-ci-execution-v1" &&
            (Status != Aggregate(values.Select(row => row.Status)) || values.Any(row => row.Status != Aggregate(row.Checks.Select(check => check.Status))))))
            throw new RegistryValidationException("status", "Stored policy outcomes are inconsistent.");
        Id = id; Policy = VerificationValues.Text(policy, "policy", 100); Profile = profile;
        Rows = Array.AsReadOnly(values.OrderBy(row => row.Selection.InputKey, StringComparer.Ordinal).ToArray());
        SourceApplicationId = source; PublisherId = RegistryIdentity.ApplicationId(publisherId, "publisherId"); EvaluatedAt = RegistryIdentity.Timestamp(now);
    }
    public static CompatibilityDecision Evaluate(VerificationProfile profile, IEnumerable<CompatibilitySelection> selections, IEnumerable<VerificationRun> evidence,
        string sourceApplicationId, string publisherId, DateTimeOffset now)
    {
        var runs = evidence.ToDictionary(run => run.ExecutionKey, StringComparer.Ordinal);
        var rows = selections.Select(selection => new CompatibilityRow(selection, profile.Checks.Select(required =>
        {
            VerificationRun? run = null;
            if (selection.BindingId is not null) runs.TryGetValue(selection.ExecutionKey(required.CheckId), out run);
            var observed = run is null ? null : new RequiredVerificationCheck(run.Execution.CheckId, run.Execution.VerifierName, run.Execution.VerifierVersion, run.Execution.ConfigurationSha256);
            var reason = selection.BindingId is null ? CompatibilityReason.ContractNotPublished : run is null ? CompatibilityReason.MissingExecution :
                observed!.Fingerprint != required.Fingerprint ? CompatibilityReason.VerifierMismatch :
                run.Provenance.SourceRef != selection.ProviderSourceRef ? CompatibilityReason.SourceRefMismatch : CompatibilityReason.Reported;
            return new CompatibilityCheckResult(required, reason == CompatibilityReason.Reported ? run!.Status : VerificationStatus.Unverified, reason,
                run?.Id, run?.PayloadSha256, run?.Status, observed, run?.Target.PublicationId, run?.Provenance.BuildUrl, run?.Provenance.PublisherId, run?.Issues.Count ?? 0);
        })));
        return new(Guid.NewGuid(), CurrentPolicy, profile, rows, sourceApplicationId, publisherId, now);
    }
    public static CompatibilityDecision Rehydrate(Guid id, string policy, VerificationProfile profile, IEnumerable<CompatibilityRow> rows,
        string sourceApplicationId, string publisherId, DateTimeOffset now, VerificationStatus status) => new(id, policy, profile, rows, sourceApplicationId, publisherId, now, status);
    public static VerificationStatus Aggregate(IEnumerable<VerificationStatus> statuses)
    {
        var values = statuses.ToArray();
        if (values.Any(value => !Enum.IsDefined(value))) throw new RegistryValidationException("status", "Invalid verification status.");
        return values.Contains(VerificationStatus.Failed) ? VerificationStatus.Failed : values.Contains(VerificationStatus.Error) ? VerificationStatus.Error :
            values.Length == 0 || values.Contains(VerificationStatus.Unverified) ? VerificationStatus.Unverified : VerificationStatus.Passed;
    }
}
