using ECommerceStore.Pact.Application;
using ECommerceStore.Pact.Api.Security;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ECommerceStore.Pact.Api.Registry;

public static class RegistryEndpoints
{
    public static IEndpointRouteBuilder MapRegistryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var versions = endpoints.MapGroup("/api/applications/{applicationId}/versions").WithTags("Application versions");
        versions.MapPost("", RegisterVersionAsync).RequireAuthorization("RegistryWrite")
            .WithName("RegisterApplicationVersion")
            .WithSummary("Register an immutable application version, or return the existing equivalent registration.")
            .ProducesValidationProblem().ProducesProblem(401).ProducesProblem(403).ProducesProblem(409).ProducesProblem(415).ProducesProblem(503);
        versions.MapGet("/{commitSha}", GetVersionAsync)
            .WithName("GetApplicationVersion")
            .WithSummary("Read a version by its application ID and full commit SHA.")
            .ProducesValidationProblem().ProducesProblem(404).ProducesProblem(503);

        var integrations = endpoints.MapGroup("/api/integrations").WithTags("Integrations");
        integrations.MapPost("", RegisterIntegrationAsync).RequireAuthorization("RegistryWrite")
            .WithName("RegisterIntegration")
            .WithSummary("Declare a directed consumer-to-provider integration; duplicate registration is idempotent.")
            .ProducesValidationProblem().ProducesProblem(401).ProducesProblem(403).ProducesProblem(415).ProducesProblem(503);
        integrations.MapGet("/{consumerApplicationId}/{providerApplicationId}", GetIntegrationAsync)
            .WithName("GetIntegration")
            .WithSummary("Read a declared integration. Registration is not verification evidence.")
            .ProducesValidationProblem().ProducesProblem(404).ProducesProblem(503);
        return endpoints;
    }

    private static async Task<Results<Created<ApplicationVersionResultDto>, Ok<ApplicationVersionResultDto>>> RegisterVersionAsync(
        string applicationId, RegisterApplicationVersionRequest request, ApplicationVersionService service,
        HttpContext context, CancellationToken cancellationToken)
    {
        PublisherAccess.RequireApplication(context.User, applicationId);
        var registration = await service.RegisterAsync(applicationId, request.CommitSha, request.RepositoryUrl,
            request.ImageDigest, cancellationToken);
        var result = ApplicationVersionResultDto.FromDomain(registration.Value);
        return registration.Created
            ? TypedResults.Created($"/api/applications/{result.ApplicationId}/versions/{result.CommitSha}", result)
            : TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<ApplicationVersionResultDto>, ProblemHttpResult>> GetVersionAsync(
        string applicationId, string commitSha, ApplicationVersionService service, CancellationToken cancellationToken)
    {
        var version = await service.GetAsync(applicationId, commitSha, cancellationToken);
        return version is null
            ? TypedResults.Problem(statusCode: 404, title: "Application version not found.",
                detail: "No version is registered for this application and commit.")
            : TypedResults.Ok(ApplicationVersionResultDto.FromDomain(version));
    }

    private static async Task<Results<Created<IntegrationResultDto>, Ok<IntegrationResultDto>>> RegisterIntegrationAsync(
        RegisterIntegrationRequest request, IntegrationService service, HttpContext context, CancellationToken cancellationToken)
    {
        PublisherAccess.RequireApplication(context.User, request.ConsumerApplicationId);
        var registration = await service.RegisterAsync(request.ConsumerApplicationId, request.ProviderApplicationId, cancellationToken);
        var result = IntegrationResultDto.FromDomain(registration.Value);
        return registration.Created
            ? TypedResults.Created($"/api/integrations/{result.ConsumerApplicationId}/{result.ProviderApplicationId}", result)
            : TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<IntegrationResultDto>, ProblemHttpResult>> GetIntegrationAsync(
        string consumerApplicationId, string providerApplicationId, IntegrationService service, CancellationToken cancellationToken)
    {
        var integration = await service.GetAsync(consumerApplicationId, providerApplicationId, cancellationToken);
        return integration is null
            ? TypedResults.Problem(statusCode: 404, title: "Integration not found.", detail: "This directed integration is not registered.")
            : TypedResults.Ok(IntegrationResultDto.FromDomain(integration));
    }
}
