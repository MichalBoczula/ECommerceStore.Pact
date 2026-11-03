using ECommerceStore.Pact.Domain;
using ECommerceStore.Pact.Domain.Compatibility;

namespace ECommerceStore.Pact.Application.Reporting;

public sealed class RegistryReadService(IRegistryReadRepository repository, TimeProvider clock)
{
    public Task<RegistryPage<RegisteredApplication>> ApplicationsAsync(RegistryReadFilter filter, int? limit, string? after, CancellationToken cancellationToken) =>
        Read(RegistryResource.Applications, filter, limit, after, repository.ApplicationsAsync, cancellationToken);
    public Task<RegistryPage<ApplicationVersion>> VersionsAsync(RegistryReadFilter filter, int? limit, string? after, CancellationToken cancellationToken) =>
        Read(RegistryResource.Versions, filter, limit, after, repository.VersionsAsync, cancellationToken);
    public Task<RegistryPage<Integration>> IntegrationsAsync(RegistryReadFilter filter, int? limit, string? after, CancellationToken cancellationToken) =>
        Read(RegistryResource.Integrations, filter, limit, after, repository.IntegrationsAsync, cancellationToken);
    public Task<RegistryPage<PublicationSummary>> PublicationsAsync(RegistryReadFilter filter, int? limit, string? after, CancellationToken cancellationToken) =>
        Read(RegistryResource.Publications, filter, limit, after, repository.PublicationsAsync, cancellationToken);
    public Task<RegistryPage<VerificationSummary>> RunsAsync(RegistryReadFilter filter, int? limit, string? after, CancellationToken cancellationToken) =>
        Read(RegistryResource.Runs, filter, limit, after, repository.RunsAsync, cancellationToken);
    public Task<RegistryPage<VerificationProfile>> ProfilesAsync(RegistryReadFilter filter, int? limit, string? after, CancellationToken cancellationToken) =>
        Read(RegistryResource.Profiles, filter, limit, after, repository.ProfilesAsync, cancellationToken);
    public Task<RegistryPage<DecisionSummary>> DecisionsAsync(RegistryReadFilter filter, int? limit, string? after, CancellationToken cancellationToken) =>
        Read(RegistryResource.Decisions, filter, limit, after, repository.DecisionsAsync, cancellationToken);
    public Task<RegistryPage<ComparisonSummary>> ComparisonsAsync(RegistryReadFilter filter, int? limit, string? after, CancellationToken cancellationToken) =>
        Read(RegistryResource.Comparisons, filter, limit, after, repository.ComparisonsAsync, cancellationToken);
    public Task<RegistryPage<DependencyEvidence>> DependenciesAsync(RegistryReadFilter filter, int? limit, string? after, CancellationToken cancellationToken) =>
        Read(RegistryResource.Dependencies, filter, limit, after, repository.DependenciesAsync, cancellationToken);

    public async Task<RegistryOverview> OverviewAsync(RegistryReadFilter filter, CancellationToken cancellationToken)
    {
        var normalized = RegistryReadQuery.Create(RegistryResource.Overview, filter).Filter;
        var inventory = await repository.InventoryAsync(cancellationToken);
        var reports = await repository.ReportsAsync(normalized, cancellationToken);
        return new RegistryOverview(inventory, reports, normalized, clock.GetUtcNow());
    }

    private static async Task<RegistryPage<T>> Read<T>(RegistryResource resource, RegistryReadFilter filter, int? limit, string? after,
        Func<RegistryReadQuery, CancellationToken, Task<RegistrySlice<T>>> read, CancellationToken cancellationToken)
    {
        var query = RegistryReadQuery.Create(resource, filter, limit, after);
        return query.Page(await read(query, cancellationToken));
    }
}
