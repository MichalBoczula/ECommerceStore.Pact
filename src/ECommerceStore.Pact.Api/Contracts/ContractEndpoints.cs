using ECommerceStore.Pact.Api.Security;
using ECommerceStore.Pact.Application.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;

namespace ECommerceStore.Pact.Api.Contracts;

public static class ContractEndpoints
{
    public static IEndpointRouteBuilder MapContractEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api").WithTags("Contract publications");
        group.MapPost("/contract-publications", PublishAsync).RequireAuthorization("RegistryWrite")
            .Accepts<PublishContractRequest>("application/json").WithName("PublishContract")
            .WithSummary("Publish original UTF-8 JSON bytes as base64 with source version and build provenance.")
            .ProducesValidationProblem().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404)
            .ProducesProblem(409).ProducesProblem(413).ProducesProblem(415).ProducesProblem(503);
        group.MapGet("/contract-publications/{id:guid}", GetPublicationAsync).WithName("GetContractPublication")
            .ProducesProblem(404).ProducesProblem(503);
        group.MapGet("/artifacts/{sha256}", GetArtifactAsync).WithName("GetContractArtifact")
            .ProducesValidationProblem().ProducesProblem(404).ProducesProblem(503);
        group.MapGet("/artifacts/{sha256}/content", DownloadAsync).WithName("DownloadContractContent")
            .WithSummary("Download the original bytes without JSON reserialization.")
            .Produces(200, contentType: "application/json").ProducesValidationProblem().ProducesProblem(404).ProducesProblem(503);
        return endpoints;
    }

    private static async Task<Results<Created<ContractPublicationResultDto>, Ok<ContractPublicationResultDto>>> PublishAsync(
        HttpContext context, ContractPublicationService service, CancellationToken cancellationToken)
    {
        var request = await ContractUploadReader.ReadAsync(context.Request, cancellationToken);
        PublisherAccess.RequireApplication(context.User, request.SourceApplicationId);
        var publication = await service.PublishAsync(request.ToCommand(), PublisherAccess.PublisherId(context.User), cancellationToken);
        var result = ContractPublicationResultDto.FromDomain(publication.Value);
        return publication.Created ? TypedResults.Created($"/api/contract-publications/{result.Id}", result) : TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<ContractPublicationResultDto>, ProblemHttpResult>> GetPublicationAsync(Guid id,
        ContractPublicationService service, CancellationToken cancellationToken)
    {
        var value = await service.GetPublicationAsync(id, cancellationToken);
        return value is null ? Missing() : TypedResults.Ok(ContractPublicationResultDto.FromDomain(value));
    }

    private static async Task<Results<Ok<ContractArtifactResultDto>, ProblemHttpResult>> GetArtifactAsync(string sha256,
        ContractPublicationService service, CancellationToken cancellationToken)
    {
        var value = await service.GetArtifactAsync(sha256, cancellationToken);
        return value is null ? Missing() : TypedResults.Ok(ContractArtifactResultDto.FromDomain(value));
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> DownloadAsync(string sha256,
        HttpContext context, ContractPublicationService service, CancellationToken cancellationToken)
    {
        var value = await service.GetArtifactAsync(sha256, cancellationToken);
        if (value is null) return Missing();
        context.Response.Headers.XContentTypeOptions = "nosniff";
        return TypedResults.File(value.GetContent(), "application/json; charset=utf-8", value.Sha256 + ".json",
            entityTag: new EntityTagHeaderValue('"' + value.Sha256 + '"'));
    }

    private static ProblemHttpResult Missing() => TypedResults.Problem(statusCode: 404, title: "Contract record not found.");
}
