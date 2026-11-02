using ECommerceStore.Pact.Domain;
using ECommerceStore.Pact.Domain.Contracts;
using ECommerceStore.Pact.Domain.Verification;

namespace ECommerceStore.Pact.Domain.Tests;

public sealed class VerificationRulesTests
{
    private const string Repository = "https://github.com/example/invoice";
    private readonly VerificationTarget _target = new(Guid.NewGuid(), new("payments", new string('a', 40)), Guid.NewGuid(),
        new("invoice", new string('b', 40)), Guid.NewGuid(), Guid.NewGuid(), new string('c', 64));
    private readonly VerificationExecution _execution = new("123", 1, "pact/orders", "pact-core", "1.0", new string('d', 64));

    [Theory]
    [InlineData(VerificationStatus.Passed, 2, 0, false, false)]
    [InlineData(VerificationStatus.Failed, 0, 1, true, false)]
    [InlineData(VerificationStatus.Error, 1, 0, false, true)]
    [InlineData(VerificationStatus.Error, 0, 1, true, true)]
    [InlineData(VerificationStatus.Unverified, 0, 0, false, false)]
    public void AcceptsDistinctStatusesAndPartialInfrastructureFailures(VerificationStatus status, int verified, int failed, bool mismatch, bool error) =>
        Assert.Equal(status, Run(status, verified, failed, Issues(mismatch, error)).Status);

    [Theory]
    [InlineData(VerificationStatus.Passed, 1, 0, false, false)]
    [InlineData(VerificationStatus.Passed, 1, 1, true, false)]
    [InlineData(VerificationStatus.Failed, 0, 0, true, false)]
    [InlineData(VerificationStatus.Failed, 1, 1, true, true)]
    [InlineData(VerificationStatus.Failed, 0, 1, false, false)]
    [InlineData(VerificationStatus.Error, 0, 0, false, false)]
    [InlineData(VerificationStatus.Unverified, 1, 0, false, false)]
    [InlineData(VerificationStatus.Unverified, 0, 0, false, true)]
    [InlineData(VerificationStatus.Passed, 3, 0, false, false)]
    [InlineData(VerificationStatus.Failed, -1, 1, true, false)]
    public void RejectsContradictoryOrIncompleteReports(VerificationStatus status, int verified, int failed, bool mismatch, bool error) =>
        Assert.Throws<RegistryValidationException>(() => Run(status, verified, failed, Issues(mismatch, error)));

    [Fact]
    public void MissingActualAndExplicitNullRemainDistinctAndNumbersKeepPrecision()
    {
        var missing = Mismatch();
        var actualNull = new VerificationIssue(VerificationIssueKind.Mismatch, "missing", "Amount missing", "read an order", "get", "/orders/123",
            "$.body.totalAmount", "12345678901234567890.123456789", true, "null");
        Assert.False(missing.ActualPresent);
        Assert.Null(missing.ActualJson);
        Assert.True(actualNull.ActualPresent);
        Assert.Equal("null", actualNull.ActualJson);
        Assert.Equal("12345678901234567890.123456789", actualNull.ExpectedJson);
        Assert.NotEqual(VerificationValues.Hash(missing), VerificationValues.Hash(actualNull));
    }

    [Fact]
    public void CanonicalDiagnosticsIgnoreObjectOrderButRejectDuplicateKeys()
    {
        Assert.Equal(VerificationValues.Json("{\"b\":2,\"a\":1}", "expected"), VerificationValues.Json("{\"a\":1,\"b\":2}", "expected"));
        Assert.Throws<RegistryValidationException>(() => VerificationValues.Json("{\"a\":1,\"a\":2}", "expected"));
        Assert.Throws<RegistryValidationException>(() => VerificationValues.Json("\"" + new string('<', 2000) + "\"", "expected"));
        Assert.Throws<RegistryValidationException>(() => VerificationValues.Json(new string(' ', 8193), "expected"));
    }

    [Fact]
    public void IdentityExcludesReportAndCredentialButPayloadDetectsImmutableChange()
    {
        var first = Run(VerificationStatus.Passed, 2, 0, []);
        var retry = Run(VerificationStatus.Passed, 2, 0, [], "rotated-ci");
        var changed = Run(VerificationStatus.Failed, 1, 1, Issues(true, false));
        Assert.NotEqual(first.Id, retry.Id);
        Assert.Equal(first.ExecutionKey, retry.ExecutionKey);
        Assert.Equal(first.PayloadSha256, retry.PayloadSha256);
        Assert.Equal(first.ExecutionKey, changed.ExecutionKey);
        Assert.NotEqual(first.PayloadSha256, changed.PayloadSha256);
    }

    [Fact]
    public void EvidenceAndCollectionsAreBoundedAndCopied()
    {
        var list = Issues(true, false).ToList();
        var run = Run(VerificationStatus.Failed, 1, 1, list);
        list.Clear();
        Assert.Single(run.Issues);
        Assert.Throws<RegistryValidationException>(() => Run(VerificationStatus.Failed, 1, 1, Enumerable.Repeat(Mismatch(), 101)));
        Assert.Throws<RegistryValidationException>(() => VerificationValues.EvidenceUrl("https://example.com/log?token=secret"));
        Assert.Equal("https://example.com/log#step:1", VerificationValues.EvidenceUrl("https://example.com/log#step:1"));
    }

    private VerificationRun Run(VerificationStatus status, int verified, int failed, IEnumerable<VerificationIssue> issues, string publisher = "invoice-ci") =>
        VerificationRun.Create(_target, _execution, new("refs/heads/main", Repository + "/actions/runs/123", publisher, Repository),
            Repository, status, 2, verified, failed, "Synthetic result", issues, [], DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddSeconds(1), DateTimeOffset.UtcNow);
    private static VerificationIssue Mismatch() => new(VerificationIssueKind.Mismatch, "missing", "Amount missing", "read an order", "GET",
        "/orders/123", "$.body.totalAmount", "12.34", false);
    private static IEnumerable<VerificationIssue> Issues(bool mismatch, bool error)
    {
        if (mismatch) yield return Mismatch();
        if (error) yield return new(VerificationIssueKind.ExecutionError, "timeout", "Provider did not start");
    }
}
