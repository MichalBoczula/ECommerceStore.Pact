using ECommerceStore.Pact.Application.Contracts;
using ECommerceStore.Pact.Application.Verification;
using ECommerceStore.Pact.Domain;
using ECommerceStore.Pact.Domain.History;
using ECommerceStore.Pact.Domain.Verification;

namespace ECommerceStore.Pact.Application.History;

public sealed record IssueHistoryReport(VerificationRun Before, VerificationRun After, IssueHistory History);

public sealed class IssueHistoryService(IVerificationRunRepository runs)
{
    public async Task<IssueHistoryReport> GetAsync(Guid beforeRunId, Guid afterRunId, string baselineRef, CancellationToken cancellationToken)
    {
        if (beforeRunId == Guid.Empty || afterRunId == Guid.Empty) throw new RegistryValidationException("runIds", "Select two nonempty run IDs.");
        var before = await runs.GetAsync(beforeRunId, cancellationToken) ?? throw new RegistryReferenceNotFoundException("baseline_run_not_found");
        var after = await runs.GetAsync(afterRunId, cancellationToken) ?? throw new RegistryReferenceNotFoundException("candidate_run_not_found");
        return new(before, after, IssueHistory.Compare(before, after, baselineRef));
    }
}
