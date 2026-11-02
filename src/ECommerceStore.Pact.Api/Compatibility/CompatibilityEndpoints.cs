using ECommerceStore.Pact.Api.Registry;
using ECommerceStore.Pact.Api.Security;
using ECommerceStore.Pact.Application.Compatibility;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ECommerceStore.Pact.Api.Compatibility;

public static class CompatibilityEndpoints
{
    public static IEndpointRouteBuilder MapCompatibilityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api").WithTags("Explicit-version compatibility");
        group.MapPost("/verification-profiles", RegisterAsync).RequireAuthorization("RegistryWrite").WithName("RegisterVerificationProfile")
            .Accepts<RegisterVerificationProfileRequest>("application/json").WithSummary("Register consumer-owned immutable required checks; a changed check set needs a new profile version.")
            .ProducesValidationProblem().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(413).ProducesProblem(415).ProducesProblem(503);
        group.MapGet("/verification-profiles/{id:guid}", GetProfileAsync).WithName("GetVerificationProfile").ProducesProblem(404).ProducesProblem(503);
        group.MapPost("/compatibility-decisions", EvaluateAsync).RequireAuthorization("RegistryWrite").WithName("EvaluateCompatibility")
            .Accepts<EvaluateCompatibilityRequest>("application/json").WithSummary("Snapshot a bounded exact-version matrix for a pinned profile and explicit CI execution per pair.")
            .ProducesValidationProblem().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(413).ProducesProblem(415).ProducesProblem(503);
        group.MapGet("/compatibility-decisions/{id:guid}", GetDecisionAsync).WithName("GetCompatibilityDecision").ProducesProblem(404).ProducesProblem(503);
        return endpoints;
    }
    private static async Task<Results<Created<VerificationProfileResultDto>, Ok<VerificationProfileResultDto>>> RegisterAsync(HttpContext context,
        VerificationProfileService service, CancellationToken cancellationToken)
    {
        var request = await BoundedJsonRequestReader.ReadAsync<RegisterVerificationProfileRequest>(context.Request, 1024 * 1024, () => new RegistryRequestTooLargeException(), cancellationToken);
        PublisherAccess.RequireApplication(context.User, request.ConsumerApplicationId);
        var result = await service.RegisterAsync(request.ToCommand(), PublisherAccess.PublisherId(context.User), cancellationToken);
        var dto = VerificationProfileResultDto.FromDomain(result.Value);
        return result.Created ? TypedResults.Created($"/api/verification-profiles/{dto.Id}", dto) : TypedResults.Ok(dto);
    }
    private static async Task<Results<Created<CompatibilityDecisionResultDto>, Ok<CompatibilityDecisionResultDto>>> EvaluateAsync(HttpContext context,
        CompatibilityDecisionService service, CancellationToken cancellationToken)
    {
        var request = await BoundedJsonRequestReader.ReadAsync<EvaluateCompatibilityRequest>(context.Request, 1024 * 1024, () => new RegistryRequestTooLargeException(), cancellationToken);
        PublisherAccess.RequireApplication(context.User, request.SourceApplicationId);
        var result = await service.EvaluateAsync(request.ToCommand(), PublisherAccess.PublisherId(context.User), cancellationToken);
        var dto = CompatibilityDecisionResultDto.FromDomain(result.Value);
        return result.Created ? TypedResults.Created($"/api/compatibility-decisions/{dto.Id}", dto) : TypedResults.Ok(dto);
    }
    private static async Task<Results<Ok<VerificationProfileResultDto>, ProblemHttpResult>> GetProfileAsync(Guid id, VerificationProfileService service, CancellationToken cancellationToken)
    {
        var value = await service.GetAsync(id, cancellationToken);
        return value is null ? TypedResults.Problem(statusCode: 404, title: "Verification profile not found.") : TypedResults.Ok(VerificationProfileResultDto.FromDomain(value));
    }
    private static async Task<Results<Ok<CompatibilityDecisionResultDto>, ProblemHttpResult>> GetDecisionAsync(Guid id, CompatibilityDecisionService service, CancellationToken cancellationToken)
    {
        var value = await service.GetAsync(id, cancellationToken);
        return value is null ? TypedResults.Problem(statusCode: 404, title: "Compatibility decision not found.") : TypedResults.Ok(CompatibilityDecisionResultDto.FromDomain(value));
    }
}
