using System.Globalization;
using ECommerceStore.Pact.Api.Compatibility;
using ECommerceStore.Pact.Api.Registry;
using ECommerceStore.Pact.Application.Reporting;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ECommerceStore.Pact.Api.Reporting;

public static class RegistryReadEndpoints
{
    private const string EvidencePolicy = "RecordedReportsOnly-v1; not a compatibility decision; not a transactional snapshot";

    public static IEndpointRouteBuilder MapRegistryReadEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api").WithTags("Registry browsing and reports");
        Describe(group.MapGet("/applications", Applications), "ListApplications", "Browse registered applications by normalized ID prefix.");
        Describe(group.MapGet("/applications/{applicationId}/versions", Versions), "ListApplicationVersions", "Browse exact registered versions; newest registration then ID. No version is selected as compatible.");
        Describe(group.MapGet("/integrations", Integrations), "ListIntegrations", "Browse directed declarations, including relationships without verification evidence.");
        Describe(group.MapGet("/contract-publications", Publications), "ListContractPublications", "Browse publication metadata without loading artifact bytes.");
        Describe(group.MapGet("/reports/verification-runs", Runs), "BrowseVerificationRuns", "Browse recorded executions and issue matches without selecting compatibility evidence.");
        Describe(group.MapGet("/verification-profiles", Profiles), "ListVerificationProfiles", "Browse immutable required-check profiles; selection remains explicit.");
        Describe(group.MapGet("/compatibility-decisions", Decisions), "ListCompatibilityDecisions", "Browse stored decisions; version/hash filters must match the same matrix row.");
        Describe(group.MapGet("/contract-comparisons", Comparisons), "ListContractComparisons", "Browse stored schema/content comparisons without recomputing them.");
        Describe(group.MapGet("/reports/dependencies", Dependencies), "GetDependencyGraph", "Browse a page of declared edges and matching report counts; not runtime traces or compatibility outcomes.");
        Describe(group.MapGet("/reports/overview", Overview), "GetRegistryOverview", "Global inventory and separately filtered recorded-report counts; not a transactional snapshot.");
        return endpoints;
    }

    private static void Describe(RouteHandlerBuilder builder, string name, string summary) =>
        builder.WithName(name).WithSummary(summary).ProducesValidationProblem().ProducesProblem(503);

    private static async Task<Ok<RegistryPageResultDto<RegisteredApplicationResultDto>>> Applications([AsParameters] ApplicationsReadRequest request,
        RegistryReadService service, CancellationToken cancellationToken) => TypedResults.Ok(Map(
        await service.ApplicationsAsync(new() { Search = request.Search }, request.Limit, request.After, cancellationToken),
        v => new RegisteredApplicationResultDto(v.ApplicationId, v.VersionCount, $"/api/applications/{v.ApplicationId}/versions")));

    private static async Task<Ok<RegistryPageResultDto<ApplicationVersionResultDto>>> Versions(string applicationId, [AsParameters] PageReadRequest request,
        RegistryReadService service, CancellationToken cancellationToken) => TypedResults.Ok(Map(
        await service.VersionsAsync(new() { ApplicationId = applicationId }, request.Limit, request.After, cancellationToken), ApplicationVersionResultDto.FromDomain));

    private static async Task<Ok<RegistryPageResultDto<IntegrationResultDto>>> Integrations([AsParameters] IntegrationsReadRequest request,
        RegistryReadService service, CancellationToken cancellationToken) => TypedResults.Ok(Map(
        await service.IntegrationsAsync(request.Filter(), request.Limit, request.After, cancellationToken), IntegrationResultDto.FromDomain));

    private static async Task<Ok<RegistryPageResultDto<PublicationSummaryResultDto>>> Publications([AsParameters] PublicationsReadRequest request,
        RegistryReadService service, CancellationToken cancellationToken) => TypedResults.Ok(Map(
        await service.PublicationsAsync(request.Filter(), request.Limit, request.After, cancellationToken), PublicationSummaryResultDto.FromRead));

    private static async Task<Ok<RegistryPageResultDto<VerificationSummaryResultDto>>> Runs([AsParameters] EvidenceReadRequest request,
        RegistryReadService service, CancellationToken cancellationToken) => TypedResults.Ok(Map(
        await service.RunsAsync(request.EvidenceFilter(), request.Limit, request.After, cancellationToken), VerificationSummaryResultDto.FromRead));

    private static async Task<Ok<RegistryPageResultDto<VerificationProfileResultDto>>> Profiles([AsParameters] ProfilesReadRequest request,
        RegistryReadService service, CancellationToken cancellationToken) => TypedResults.Ok(Map(
        await service.ProfilesAsync(request.Filter(), request.Limit, request.After, cancellationToken), VerificationProfileResultDto.FromDomain));

    private static async Task<Ok<RegistryPageResultDto<DecisionSummaryResultDto>>> Decisions([AsParameters] DecisionsReadRequest request,
        RegistryReadService service, CancellationToken cancellationToken) => TypedResults.Ok(Map(
        await service.DecisionsAsync(request.Filter(), request.Limit, request.After, cancellationToken), DecisionSummaryResultDto.FromRead));

    private static async Task<Ok<RegistryPageResultDto<ComparisonSummaryResultDto>>> Comparisons([AsParameters] ComparisonsReadRequest request,
        RegistryReadService service, CancellationToken cancellationToken) => TypedResults.Ok(Map(
        await service.ComparisonsAsync(request.Filter(), request.Limit, request.After, cancellationToken), ComparisonSummaryResultDto.FromRead));

    private static async Task<Ok<DependencyGraphResultDto>> Dependencies([AsParameters] EvidenceReadRequest request,
        RegistryReadService service, CancellationToken cancellationToken)
    {
        var filter = RegistryReadQuery.Create(RegistryResource.Dependencies, request.EvidenceFilter()).Filter;
        var page = await service.DependenciesAsync(filter, request.Limit, request.After, cancellationToken);
        var nodes = page.Items.SelectMany(e => new[] { e.Integration.Identity.ConsumerApplicationId, e.Integration.Identity.ProviderApplicationId })
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(id => new DependencyNodeResultDto(id, $"/api/applications/{id}/versions")).ToArray();
        var edges = page.Items.Select(e =>
        {
            var identity = e.Integration.Identity;
            var selection = filter with { ApplicationId = null, ConsumerApplicationId = identity.ConsumerApplicationId, ProviderApplicationId = identity.ProviderApplicationId };
            return new DependencyEdgeResultDto(IntegrationResultDto.FromDomain(e.Integration), "Declared", VerificationCountsResultDto.FromRead(e.Reports),
                $"/api/integrations/{identity.ConsumerApplicationId}/{identity.ProviderApplicationId}", ReportsUrl(selection),
                $"/api/contract-publications?consumerApplicationId={identity.ConsumerApplicationId}&providerApplicationId={identity.ProviderApplicationId}");
        }).ToArray();
        return TypedResults.Ok(new DependencyGraphResultDto(nodes, edges, page.NextCursor, EvidenceSelectionResultDto.FromRead(filter), EvidencePolicy, request.After is null && page.NextCursor is null));
    }

    private static async Task<Ok<RegistryOverviewResultDto>> Overview([AsParameters] EvidenceSelectionRequest request,
        RegistryReadService service, CancellationToken cancellationToken)
    {
        var result = await service.OverviewAsync(request.EvidenceFilter(), cancellationToken);
        var i = result.Inventory;
        return TypedResults.Ok(new RegistryOverviewResultDto(new(i.RegisteredApplications, i.ApplicationVersions, i.DeclaredIntegrations, i.ContractPublications,
            i.VerificationProfiles, i.ContractComparisons, i.CompatibilityDecisions), VerificationCountsResultDto.FromRead(result.Reports),
            EvidenceSelectionResultDto.FromRead(result.Selection), EvidencePolicy, result.ObservedAt));
    }

    private static RegistryPageResultDto<TDto> Map<T, TDto>(RegistryPage<T> page, Func<T, TDto> map) => new(page.Items.Select(map).ToArray(), page.NextCursor);

    private static string ReportsUrl(RegistryReadFilter f)
    {
        (string Key, string? Value)[] values = [
            ("consumerApplicationId", f.ConsumerApplicationId), ("providerApplicationId", f.ProviderApplicationId),
            ("consumerCommitSha", f.ConsumerCommitSha), ("providerCommitSha", f.ProviderCommitSha), ("contractSha256", f.ContractSha256),
            ("sourceRef", f.SourceRef), ("status", f.Status), ("issueCode", f.IssueCode), ("jsonPath", f.JsonPath),
            ("ciRunId", f.CiRunId), ("ciAttempt", f.CiAttempt?.ToString(CultureInfo.InvariantCulture)), ("ciCheckId", f.CiCheckId),
            ("verifierName", f.VerifierName), ("verifierVersion", f.VerifierVersion), ("configurationSha256", f.ConfigurationSha256)
        ];
        return "/api/reports/verification-runs?" + string.Join('&', values.Where(v => v.Value is not null).Select(v => $"{v.Key}={Uri.EscapeDataString(v.Value!)}"));
    }
}
