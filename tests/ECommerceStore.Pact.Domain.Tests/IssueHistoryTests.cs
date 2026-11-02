using ECommerceStore.Pact.Domain;
using ECommerceStore.Pact.Domain.Contracts;
using ECommerceStore.Pact.Domain.History;
using ECommerceStore.Pact.Domain.Verification;

namespace ECommerceStore.Pact.Domain.Tests;

public sealed class IssueHistoryTests
{
    [Theory]
    [InlineData(VerificationStatus.Failed, VerificationStatus.Failed, IssueTransition.Persisting)]
    [InlineData(VerificationStatus.Failed, VerificationStatus.Passed, IssueTransition.Resolved)]
    [InlineData(VerificationStatus.Passed, VerificationStatus.Failed, IssueTransition.Appeared)]
    [InlineData(VerificationStatus.Unverified, VerificationStatus.Failed, IssueTransition.Observed)]
    [InlineData(VerificationStatus.Error, VerificationStatus.Failed, IssueTransition.Observed)]
    [InlineData(VerificationStatus.Failed, VerificationStatus.Error, IssueTransition.Unconfirmed)]
    [InlineData(VerificationStatus.Failed, VerificationStatus.Unverified, IssueTransition.Unconfirmed)]
    public void TransitionsRequireComparablePositiveEvidence(VerificationStatus before, VerificationStatus after, IssueTransition transition)
    {
        var history = IssueHistory.Compare(Run(before), Run(after), "refs/heads/main");
        Assert.True(history.Comparable);
        Assert.Equal(transition, Assert.Single(history.Issues).Transition);
    }

    [Theory]
    [InlineData("requirements", IssueTransition.RequirementsChanged)]
    [InlineData("verifier", IssueTransition.VerifierChanged)]
    [InlineData("version", IssueTransition.VerifierChanged)]
    [InlineData("configuration", IssueTransition.VerifierChanged)]
    [InlineData("check", IssueTransition.VerifierChanged)]
    public void ChangedRequirementsOrVerifierNeverProveProviderFixedAnIssue(string change, IssueTransition expected)
    {
        var history = IssueHistory.Compare(Run(VerificationStatus.Failed), Run(VerificationStatus.Passed, change), "refs/heads/main");
        Assert.False(history.Comparable);
        Assert.Equal(expected, Assert.Single(history.Issues).Transition);
    }

    [Fact]
    public void MessageAndActualValuesDoNotChangeRequirementIdentityButExpectedValuesDo()
    {
        var first = Run(VerificationStatus.Failed);
        var second = Run(VerificationStatus.Failed, "actual");
        Assert.Equal(IssueTransition.Persisting, Assert.Single(IssueHistory.Compare(first, second, "refs/heads/main").Issues).Transition);
        var changedDiagnostic = IssueHistory.Compare(first, Run(VerificationStatus.Failed, "expected"), "refs/heads/main");
        Assert.Contains(changedDiagnostic.Issues, issue => issue.Transition == IssueTransition.Unconfirmed);
        Assert.DoesNotContain(changedDiagnostic.Issues, issue => issue.Transition == IssueTransition.Resolved);
    }

    [Fact]
    public void ExplicitBaselineAndIntegrationAreRequired()
    {
        Assert.Throws<RegistryValidationException>(() => IssueHistory.Compare(Run(VerificationStatus.Failed), Run(VerificationStatus.Passed), "refs/heads/other"));
        Assert.Throws<RegistryValidationException>(() => IssueHistory.Compare(Run(VerificationStatus.Failed), Run(VerificationStatus.Passed, "integration"), "refs/heads/main"));
    }

    private static VerificationRun Run(VerificationStatus status, string? change = null)
    {
        const string repository = "https://github.com/example/invoice";
        var target = new VerificationTarget(Guid.NewGuid(), new("payments", new string('a', 40)), Guid.NewGuid(),
            new(change == "integration" ? "products" : "invoice", new string('b', 40)), Guid.NewGuid(), Guid.NewGuid(), new string(change == "requirements" ? 'e' : 'c', 64));
        var execution = new VerificationExecution("1", 1, change == "check" ? "other-check" : "pact", change == "verifier" ? "other-tool" : "pact-core",
            change == "version" ? "2" : "1", new string(change == "configuration" ? 'f' : 'd', 64));
        VerificationIssue[] issues = status switch
        {
            VerificationStatus.Failed => [new(VerificationIssueKind.Mismatch, "missing", change == "actual" ? "Other message" : "Missing amount", "read an order", "GET", "/orders/123",
                "$.body.totalAmount", change == "expected" ? "24.68" : "12.34", change == "actual", change == "actual" ? "null" : null)],
            VerificationStatus.Error => [new(VerificationIssueKind.ExecutionError, "timeout", "Provider unavailable")],
            _ => []
        };
        return VerificationRun.Create(target, execution, new ContractProvenance("refs/heads/main", repository + "/actions/runs/1", "invoice-ci", repository), repository, status,
            1, status == VerificationStatus.Passed ? 1 : 0, status == VerificationStatus.Failed ? 1 : 0, "Synthetic report", issues, [], DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddSeconds(1), DateTimeOffset.UtcNow);
    }
}
