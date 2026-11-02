using ECommerceStore.Pact.Domain;
using ECommerceStore.Pact.Domain.Compatibility;
using ECommerceStore.Pact.Domain.Contracts;
using ECommerceStore.Pact.Domain.Verification;

namespace ECommerceStore.Pact.Domain.Tests;

public sealed class CompatibilityPolicyTests
{
    private const string Repository = "https://github.com/example/invoice";
    private readonly Guid _consumerId = Guid.NewGuid();
    private readonly Guid _providerId = Guid.NewGuid();
    private readonly Guid _bindingId = Guid.NewGuid();
    private readonly ApplicationVersionIdentity _consumer = new("payments", new string('a', 40));
    private readonly ApplicationVersionIdentity _provider = new("invoice", new string('b', 40));
    private readonly RequiredVerificationCheck _check = new("pact/orders", "pact-core", "1", new string('d', 64));

    [Theory]
    [InlineData(VerificationStatus.Passed)]
    [InlineData(VerificationStatus.Failed)]
    [InlineData(VerificationStatus.Error)]
    [InlineData(VerificationStatus.Unverified)]
    public void ApplicableReportedStatusIsPreserved(VerificationStatus status)
    {
        var decision = Decide([Selection()], [Run(status)]);
        Assert.Equal(status, decision.Status);
        Assert.Equal(status == VerificationStatus.Passed, decision.Compatible);
        Assert.Equal(CompatibilityReason.Reported, decision.Rows[0].Checks[0].Reason);
    }

    [Theory]
    [InlineData("verifier", CompatibilityReason.VerifierMismatch)]
    [InlineData("version", CompatibilityReason.VerifierMismatch)]
    [InlineData("configuration", CompatibilityReason.VerifierMismatch)]
    [InlineData("ref", CompatibilityReason.SourceRefMismatch)]
    public void InapplicableSuccessIsUnverifiedAndOriginalEvidenceIsVisible(string change, CompatibilityReason expected)
    {
        var run = Run(VerificationStatus.Passed, change);
        var result = Decide([Selection()], [run]);
        Assert.Equal(VerificationStatus.Unverified, result.Status);
        Assert.False(result.Compatible);
        var check = result.Rows[0].Checks[0];
        Assert.Equal(expected, check.Reason); Assert.Equal(run.Id, check.EvidenceId); Assert.Equal(VerificationStatus.Passed, check.ReportedStatus);
    }

    [Theory]
    [InlineData("run")]
    [InlineData("attempt")]
    [InlineData("check")]
    [InlineData("provider")]
    [InlineData("consumer")]
    [InlineData("hash")]
    public void DifferentExecutionVersionOrContentCannotFillMissingEvidence(string change)
    {
        var decision = Decide([Selection()], [Run(VerificationStatus.Passed, change)]);
        Assert.Equal(VerificationStatus.Unverified, decision.Status);
        Assert.Equal(CompatibilityReason.MissingExecution, decision.Rows[0].Checks[0].Reason);
        Assert.Null(decision.Rows[0].Checks[0].EvidenceId);
    }

    [Fact]
    public void EveryRequiredCheckMustPassAndEmptyProfilesAreRejected()
    {
        var profile = Profile([_check, new("pact/not-found", "pact-core", "1", new string('d', 64))]);
        var decision = CompatibilityDecision.Evaluate(profile, [Selection()], [Run(VerificationStatus.Passed)], "invoice", "ci", DateTimeOffset.UtcNow);
        Assert.Equal(VerificationStatus.Unverified, decision.Status); Assert.False(decision.Compatible);
        Assert.Equal(CompatibilityReason.MissingExecution, decision.Rows[0].Checks.Single(check => check.Required.CheckId == "pact/not-found").Reason);
        Assert.Throws<RegistryValidationException>(() => Profile([]));
        Assert.Throws<RegistryValidationException>(() => Profile([_check, _check]));
        Assert.Throws<RegistryValidationException>(() => Decide([], []));
    }

    [Theory]
    [InlineData(VerificationStatus.Passed, VerificationStatus.Unverified, VerificationStatus.Unverified)]
    [InlineData(VerificationStatus.Error, VerificationStatus.Unverified, VerificationStatus.Error)]
    [InlineData(VerificationStatus.Failed, VerificationStatus.Error, VerificationStatus.Failed)]
    [InlineData(VerificationStatus.Passed, VerificationStatus.Passed, VerificationStatus.Passed)]
    public void AggregatePolicyIsExplicitAndDoesNotHideFailure(VerificationStatus first, VerificationStatus second, VerificationStatus expected) =>
        Assert.Equal(expected, CompatibilityDecision.Aggregate([first, second]));

    [Fact]
    public void MissingContractAndNoStatusesCannotPass()
    {
        var selected = new CompatibilitySelection(_consumer, _provider, _consumerId, _providerId, null, new string('c', 64), "123", 1, "refs/heads/main");
        var decision = Decide([selected], [Run(VerificationStatus.Passed)]);
        Assert.Equal(CompatibilityReason.ContractNotPublished, decision.Rows[0].Checks[0].Reason);
        Assert.False(decision.Compatible);
        Assert.Equal(VerificationStatus.Unverified, CompatibilityDecision.Aggregate([]));
    }

    [Fact]
    public void RowAndEvidenceOrderDoNotChangeSnapshotIdentityButNewEvidenceDoes()
    {
        var profile = Profile(); var old = Run(VerificationStatus.Failed); var next = Run(VerificationStatus.Passed, "provider");
        var other = new CompatibilitySelection(_consumer, next.Target.Provider, _consumerId, next.Target.ProviderVersionId, _bindingId, new string('c', 64), "123", 1, "refs/heads/main");
        var first = CompatibilityDecision.Evaluate(profile, [Selection(), other], [old, next], "invoice", "ci", DateTimeOffset.UtcNow);
        var reordered = CompatibilityDecision.Evaluate(profile, [other, Selection()], [next, old], "payments", "other-ci", DateTimeOffset.UtcNow);
        Assert.Equal(first.Key, reordered.Key);
        var missing = CompatibilityDecision.Evaluate(profile, [Selection(), other], [old], "invoice", "ci", DateTimeOffset.UtcNow);
        Assert.NotEqual(first.Key, missing.Key);
        Assert.Throws<RegistryValidationException>(() => CompatibilityDecision.Evaluate(profile, [Selection(), Selection()], [], "invoice", "ci", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void HistoricalOutcomesAreStoredAlongWithTheirPolicyVersion()
    {
        var original = Decide([Selection()], [Run(VerificationStatus.Passed)]);
        var historicalRow = new CompatibilityRow(original.Rows[0].Selection, original.Rows[0].Checks, VerificationStatus.Unverified);
        var historical = CompatibilityDecision.Rehydrate(original.Id, "historical-policy-v0", original.Profile, [historicalRow],
            original.SourceApplicationId, original.PublisherId, original.EvaluatedAt, VerificationStatus.Unverified);
        Assert.Equal("historical-policy-v0", historical.Policy); Assert.Equal(VerificationStatus.Unverified, historical.Status);
        Assert.Equal(VerificationStatus.Unverified, historical.Rows[0].Status); Assert.False(historical.Compatible);
    }

    [Fact]
    public void ProfileContentsAreCopiedAndCanonicalized()
    {
        var checks = new List<RequiredVerificationCheck> { _check, new("another", "pact-core", "1", new string('d', 64)) };
        var profile = Profile(checks); var reversed = Profile(checks.AsEnumerable().Reverse()); checks.Clear();
        Assert.Equal(2, profile.Checks.Count); Assert.Equal(profile.Key, reversed.Key); Assert.Equal(profile.ContentSha256, reversed.ContentSha256);
    }
    private VerificationProfile Profile(IEnumerable<RequiredVerificationCheck>? checks = null) => VerificationProfile.Create(new("payments", "invoice"), "required", "1", checks ?? [_check], "payments-ci", DateTimeOffset.UtcNow);
    private CompatibilitySelection Selection() => new(_consumer, _provider, _consumerId, _providerId, _bindingId, new string('c', 64), "123", 1, "refs/heads/main");
    private CompatibilityDecision Decide(IEnumerable<CompatibilitySelection> selections, IEnumerable<VerificationRun> evidence) =>
        CompatibilityDecision.Evaluate(Profile(), selections, evidence, "invoice", "invoice-ci", DateTimeOffset.UtcNow);
    private VerificationRun Run(VerificationStatus status, string? change = null)
    {
        var target = new VerificationTarget(change == "consumer" ? Guid.NewGuid() : _consumerId, _consumer,
            change == "provider" ? Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb") : _providerId,
            change == "provider" ? new("invoice", new string('e', 40)) : _provider, Guid.NewGuid(), _bindingId, new string(change == "hash" ? 'f' : 'c', 64));
        var execution = new VerificationExecution(change == "run" ? "124" : "123", change == "attempt" ? 2 : 1, change == "check" ? "other" : "pact/orders",
            change == "verifier" ? "other-tool" : "pact-core", change == "version" ? "2" : "1", new string(change == "configuration" ? 'f' : 'd', 64));
        VerificationIssue[] issues = status switch
        {
            VerificationStatus.Failed => [new(VerificationIssueKind.Mismatch, "missing", "Missing amount", "read", "GET", "/orders/123", "$.body.totalAmount", "12.34", false)],
            VerificationStatus.Error => [new(VerificationIssueKind.ExecutionError, "timeout", "Provider unavailable")],
            _ => []
        };
        return VerificationRun.Create(target, execution, new(change == "ref" ? "refs/heads/feature" : "refs/heads/main", Repository + "/actions/runs/123", "invoice-ci", Repository), Repository,
            status, 1, status == VerificationStatus.Passed ? 1 : 0, status == VerificationStatus.Failed ? 1 : 0, "Synthetic report", issues, [], DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddSeconds(1), DateTimeOffset.UtcNow);
    }
}
