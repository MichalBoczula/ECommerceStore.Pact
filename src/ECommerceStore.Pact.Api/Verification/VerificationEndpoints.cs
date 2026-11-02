using ECommerceStore.Pact.Api.Registry;
using ECommerceStore.Pact.Api.Security;
using ECommerceStore.Pact.Application.Verification;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ECommerceStore.Pact.Api.Verification;

public static class VerificationEndpoints
{
    public static IEndpointRouteBuilder MapVerificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/verification-runs").WithTags("Verification runs");
        group.MapPost("", AppendAsync).RequireAuthorization("RegistryWrite").WithName("AppendVerificationRun")
            .Accepts<VerificationRunRequest>("application/json").WithSummary("Append a provider CI report; does not execute Pact verification.")
            .ProducesValidationProblem().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .ProducesProblem(413).ProducesProblem(415).ProducesProblem(503);
        group.MapGet("/{id:guid}", GetAsync).WithName("GetVerificationRun").ProducesProblem(404).ProducesProblem(503);
        group.MapGet("", ListAsync).WithName("ListVerificationRuns")
            .WithSummary("Read a bounded history for an explicit integration and Pact hash; ID order is not a compatibility decision.")
            .ProducesValidationProblem().ProducesProblem(503);
        return endpoints;
    }

    private static async Task<Results<Created<VerificationRunResultDto>, Ok<VerificationRunResultDto>>> AppendAsync(
        HttpContext context, VerificationRunService service, CancellationToken cancellationToken)
    {
        var request = await BoundedJsonRequestReader.ReadAsync<VerificationRunRequest>(context.Request, 1024 * 1024,
            () => new RegistryRequestTooLargeException(), cancellationToken);
        PublisherAccess.RequireApplication(context.User, request.ProviderApplicationId);
        var stored = await service.AppendAsync(request.ToCommand(), PublisherAccess.PublisherId(context.User), cancellationToken);
        var result = VerificationRunResultDto.FromDomain(stored.Value);
        return stored.Created ? TypedResults.Created($"/api/verification-runs/{result.Id}", result) : TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<VerificationRunResultDto>, ProblemHttpResult>> GetAsync(Guid id,
        VerificationRunService service, CancellationToken cancellationToken)
    {
        var run = await service.GetAsync(id, cancellationToken);
        return run is null ? TypedResults.Problem(statusCode: 404, title: "Verification run not found.") : TypedResults.Ok(VerificationRunResultDto.FromDomain(run));
    }

    private static async Task<Ok<VerificationPageResultDto>> ListAsync(string consumerApplicationId, string providerApplicationId,
        string contractSha256, int? limit, Guid? after, VerificationRunService service, CancellationToken cancellationToken)
    {
        var page = await service.ListAsync(consumerApplicationId, providerApplicationId, contractSha256, limit, after, cancellationToken);
        return TypedResults.Ok(new VerificationPageResultDto(page.Items.Select(VerificationRunResultDto.FromDomain).ToArray(), page.NextCursor));
    }
}
