using ECommerceStore.Pact.Domain;
using ECommerceStore.Pact.Domain.Compatibility;
using ECommerceStore.Pact.Domain.Verification;

namespace ECommerceStore.Pact.Infrastructure.Compatibility;

internal static class CompatibilityMapping
{
    private static RequiredCheckDocument Check(RequiredVerificationCheck value) => new()
    { CheckId = value.CheckId, VerifierName = value.VerifierName, VerifierVersion = value.VerifierVersion, ConfigurationSha256 = value.ConfigurationSha256 };
    private static RequiredVerificationCheck Check(RequiredCheckDocument value) => new(value.CheckId, value.VerifierName, value.VerifierVersion, value.ConfigurationSha256);
    internal static VerificationProfileDocument ToDocument(VerificationProfile value) => new()
    {
        Id = value.Id.ToString("D"),
        Key = value.Key,
        ContentSha256 = value.ContentSha256,
        ConsumerApplicationId = value.Integration.ConsumerApplicationId,
        ProviderApplicationId = value.Integration.ProviderApplicationId,
        Name = value.Name,
        Version = value.Version,
        Checks = value.Checks.Select(Check).ToList(),
        PublisherId = value.PublisherId,
        RegisteredAtUtc = value.RegisteredAt.UtcDateTime
    };
    internal static VerificationProfile ToDomain(VerificationProfileDocument value) => Read(() =>
    {
        var profile = VerificationProfile.Rehydrate(Guid.Parse(value.Id), new(value.ConsumerApplicationId, value.ProviderApplicationId), value.Name, value.Version,
            value.Checks.Select(Check), value.PublisherId, new(value.RegisteredAtUtc, TimeSpan.Zero));
        if (profile.Key != value.Key || profile.ContentSha256 != value.ContentSha256) throw new InvalidOperationException("Stored verification profile integrity check failed.");
        return profile;
    });
    internal static CompatibilityDecisionDocument ToDocument(CompatibilityDecision value) => new()
    {
        Id = value.Id.ToString("D"),
        Key = value.Key,
        Policy = value.Policy,
        Status = value.Status.ToString(),
        Profile = ToDocument(value.Profile),
        SourceApplicationId = value.SourceApplicationId,
        PublisherId = value.PublisherId,
        EvaluatedAtUtc = value.EvaluatedAt.UtcDateTime,
        Rows = value.Rows.Select(row => new CompatibilityRowDocument
        {
            Status = row.Status.ToString(),
            ConsumerCommitSha = row.Selection.Consumer.CommitSha,
            ProviderCommitSha = row.Selection.Provider.CommitSha,
            ConsumerVersionId = row.Selection.ConsumerVersionId.ToString("D"),
            ProviderVersionId = row.Selection.ProviderVersionId.ToString("D"),
            BindingId = row.Selection.BindingId?.ToString("D"),
            ContractSha256 = row.Selection.ContractSha256,
            CiRunId = row.Selection.CiRunId,
            CiAttempt = row.Selection.CiAttempt,
            ProviderSourceRef = row.Selection.ProviderSourceRef,
            Checks = row.Checks.Select(check => new CompatibilityCheckDocument
            {
                Required = Check(check.Required),
                Status = check.Status.ToString(),
                Reason = check.Reason.ToString(),
                EvidenceId = check.EvidenceId?.ToString("D"),
                EvidencePayloadSha256 = check.EvidencePayloadSha256,
                ReportedStatus = check.ReportedStatus?.ToString(),
                ObservedVerifier = check.ObservedVerifier is null ? null : Check(check.ObservedVerifier),
                ObservedPublicationId = check.ObservedPublicationId?.ToString("D"),
                BuildUrl = check.BuildUrl,
                PublisherId = check.PublisherId,
                IssueCount = check.IssueCount
            }).ToList()
        }).ToList()
    };
    internal static CompatibilityDecision ToDomain(CompatibilityDecisionDocument value) => Read(() =>
    {
        var profile = ToDomain(value.Profile);
        var result = CompatibilityDecision.Rehydrate(Guid.Parse(value.Id), value.Policy, profile, value.Rows.Select(row => new CompatibilityRow(
            new CompatibilitySelection(new(profile.Integration.ConsumerApplicationId, row.ConsumerCommitSha), new(profile.Integration.ProviderApplicationId, row.ProviderCommitSha),
                Guid.Parse(row.ConsumerVersionId), Guid.Parse(row.ProviderVersionId), Optional(row.BindingId), row.ContractSha256, row.CiRunId, row.CiAttempt, row.ProviderSourceRef),
            row.Checks.Select(check => new CompatibilityCheckResult(Check(check.Required), Enum.Parse<VerificationStatus>(check.Status), Enum.Parse<CompatibilityReason>(check.Reason),
                Optional(check.EvidenceId), check.EvidencePayloadSha256, check.ReportedStatus is null ? null : Enum.Parse<VerificationStatus>(check.ReportedStatus),
                check.ObservedVerifier is null ? null : Check(check.ObservedVerifier), Optional(check.ObservedPublicationId), check.BuildUrl, check.PublisherId, check.IssueCount)), Enum.Parse<VerificationStatus>(row.Status))),
            value.SourceApplicationId, value.PublisherId, new(value.EvaluatedAtUtc, TimeSpan.Zero), Enum.Parse<VerificationStatus>(value.Status));
        if (result.Key != value.Key) throw new InvalidOperationException("Stored compatibility decision integrity check failed.");
        return result;
    });
    private static Guid? Optional(string? value) => value is null ? null : Guid.Parse(value);
    private static T Read<T>(Func<T> factory)
    {
        try { return factory(); }
        catch (Exception exception) when (exception is RegistryValidationException or ArgumentException or FormatException)
        { throw new InvalidOperationException("A stored compatibility record is invalid.", exception); }
    }
}
