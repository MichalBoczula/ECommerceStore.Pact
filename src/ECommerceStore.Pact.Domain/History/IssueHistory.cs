using ECommerceStore.Pact.Domain.Verification;

namespace ECommerceStore.Pact.Domain.History;

public enum IssueTransition { Appeared, Observed, Persisting, Resolved, Unconfirmed, RequirementsChanged, VerifierChanged }
public sealed record HistoricalIssue(string Key, IssueTransition Transition, VerificationIssue? Before, VerificationIssue? After);

public sealed class IssueHistory
{
    public const string Policy = "exact-pact-requirements-v1";
    public Guid BeforeRunId { get; }
    public Guid AfterRunId { get; }
    public bool Comparable { get; }
    public string Reason { get; }
    public IReadOnlyList<HistoricalIssue> Issues { get; }

    private IssueHistory(VerificationRun before, VerificationRun after, bool comparable, string reason, IEnumerable<HistoricalIssue> issues)
    {
        BeforeRunId = before.Id; AfterRunId = after.Id; Comparable = comparable; Reason = reason;
        Issues = Array.AsReadOnly(issues.OrderBy(issue => issue.Key, StringComparer.Ordinal).ToArray());
    }

    public static IssueHistory Compare(VerificationRun before, VerificationRun after, string baselineRef)
    {
        if (before.Target.Consumer.ApplicationId != after.Target.Consumer.ApplicationId || before.Target.Provider.ApplicationId != after.Target.Provider.ApplicationId ||
            before.Provenance.SourceRef != baselineRef)
            throw new RegistryValidationException("history", "Select runs of the same integration and an explicit matching provider baseline ref.");
        var sameRequirements = before.Target.ContractSha256 == after.Target.ContractSha256;
        var sameVerifier = before.Execution.VerifierName == after.Execution.VerifierName && before.Execution.VerifierVersion == after.Execution.VerifierVersion &&
            before.Execution.ConfigurationSha256 == after.Execution.ConfigurationSha256 && before.Execution.CheckId == after.Execution.CheckId;
        var comparable = sameRequirements && sameVerifier;
        var oldIssues = Group(before); var newIssues = Group(after);
        var values = oldIssues.Keys.Union(newIssues.Keys, StringComparer.Ordinal).Select(key =>
        {
            oldIssues.TryGetValue(key, out var previous); newIssues.TryGetValue(key, out var current);
            var transition = !sameRequirements ? IssueTransition.RequirementsChanged : !sameVerifier ? IssueTransition.VerifierChanged :
                previous is not null && current is not null ? IssueTransition.Persisting :
                previous is not null ? after.Status == VerificationStatus.Passed ? IssueTransition.Resolved : IssueTransition.Unconfirmed :
                before.Status == VerificationStatus.Passed ? IssueTransition.Appeared : IssueTransition.Observed;
            return new HistoricalIssue(key, transition, previous, current);
        });
        return new IssueHistory(before, after, comparable, !sameRequirements ? "Consumer requirements changed; absence is not a provider fix." :
            !sameVerifier ? "Verifier version, configuration or check changed." : "Exact Pact content and verifier/check configuration match.", values);
    }

    // Actual values and message wording may change while the same requirement still fails.
    private static Dictionary<string, VerificationIssue> Group(VerificationRun run) => run.Issues.Where(issue => issue.Kind == VerificationIssueKind.Mismatch)
        .GroupBy(issue => VerificationValues.Hash(Policy, run.Target.Consumer.ApplicationId, run.Target.Provider.ApplicationId, issue.Kind, issue.Code, issue.Interaction, issue.Method, issue.Path, issue.JsonPath, issue.ExpectedJson))
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
}
