using ECommerceStore.Pact.Api.Registry;
using ECommerceStore.Pact.Api.Security;
using ECommerceStore.Pact.Application.History;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ECommerceStore.Pact.Api.History;

public static class HistoryEndpoints
{
    public static IEndpointRouteBuilder MapHistoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api").WithTags("Contract changes and issue history");
        group.MapPost("/contract-comparisons", CompareAsync).RequireAuthorization("RegistryWrite").WithName("CompareContracts")
            .Accepts<CompareContractsRequest>("application/json").WithSummary("Snapshot a schema/content comparison between explicit publications and a selected branch baseline.")
            .ProducesValidationProblem().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(413).ProducesProblem(415).ProducesProblem(503);
        group.MapGet("/contract-comparisons/{id:guid}", GetComparisonAsync).WithName("GetContractComparison").ProducesProblem(404).ProducesProblem(503);
        group.MapGet("/issue-history", GetHistoryAsync).WithName("CompareIssueHistory")
            .WithSummary("Project issue transitions between explicit verification runs; changed requirements never prove a provider fix.")
            .ProducesValidationProblem().ProducesProblem(404).ProducesProblem(503);
        return endpoints;
    }
    private static async Task<Results<Created<ContractComparisonResultDto>, Ok<ContractComparisonResultDto>>> CompareAsync(
        HttpContext context, ContractComparisonService service, CancellationToken cancellationToken)
    {
        var request = await BoundedJsonRequestReader.ReadAsync<CompareContractsRequest>(context.Request, 1024 * 1024,
            () => new RegistryRequestTooLargeException(), cancellationToken);
        PublisherAccess.RequireApplication(context.User, request.SourceApplicationId);
        var stored = await service.CompareAsync(request.ToCommand(), PublisherAccess.PublisherId(context.User), cancellationToken);
        var result = ContractComparisonResultDto.FromDomain(stored.Value);
        return stored.Created ? TypedResults.Created($"/api/contract-comparisons/{result.Id}", result) : TypedResults.Ok(result);
    }
    private static async Task<Results<Ok<ContractComparisonResultDto>, ProblemHttpResult>> GetComparisonAsync(Guid id,
        ContractComparisonService service, CancellationToken cancellationToken)
    {
        var value = await service.GetAsync(id, cancellationToken);
        return value is null ? TypedResults.Problem(statusCode: 404, title: "Contract comparison not found.") : TypedResults.Ok(ContractComparisonResultDto.FromDomain(value));
    }
    private static async Task<Ok<IssueHistoryResultDto>> GetHistoryAsync(Guid beforeRunId, Guid afterRunId, string baselineRef,
        IssueHistoryService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(IssueHistoryResultDto.FromReport(await service.GetAsync(beforeRunId, afterRunId, baselineRef, cancellationToken), baselineRef));
}
