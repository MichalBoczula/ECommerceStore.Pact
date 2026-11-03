using ECommerceStore.Pact.Application;
using ECommerceStore.Pact.Application.Compatibility;
using ECommerceStore.Pact.Application.History;
using ECommerceStore.Pact.Application.Verification;
using ECommerceStore.Pact.Application.Contracts;
using ECommerceStore.Pact.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceStore.Pact.Api.Registry;

public sealed class RegistryExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails? problem = exception switch
        {
            RegistryValidationException validation => Validation(validation.Field, validation.ValidationMessage),
            RegistryConflictException => new ProblemDetails
            {
                Status = 409,
                Title = "Immutable application version conflict.",
                Detail = "This application and commit are already registered with different repository or image metadata.",
                Extensions = { ["code"] = "application_version_metadata_conflict" }
            },
            VerificationConflictException => new ProblemDetails
            {
                Status = 409,
                Title = "Immutable verification report conflict.",
                Detail = "This CI run, attempt and check already has a different report for this version pair and contract.",
                Extensions = { ["code"] = "verification_execution_conflict" }
            },
            RegistryRequestTooLargeException => new ProblemDetails { Status = 413, Title = "Registry request too large.", Detail = "The JSON request envelope is limited to 1 MiB." },
            ComparisonUnavailableException => new ProblemDetails { Status = 503, Title = "Contract comparison unavailable.", Extensions = { ["code"] = "comparison_unavailable" } },
            VerificationProfileConflictException => new ProblemDetails
            {
                Status = 409,
                Title = "Immutable verification profile conflict.",
                Detail = "This integration/name/version already has different required checks. Register a new profile version.",
                Extensions = { ["code"] = "verification_profile_conflict" }
            },
            RegistryUnavailableException => new ProblemDetails { Status = 503, Title = "Registry storage unavailable." },
            ContractConflictException => new ProblemDetails
            {
                Status = 409,
                Title = "Immutable contract conflict.",
                Detail = "This source version already has different contract content for this kind and integration.",
                Extensions = { ["code"] = "contract_content_conflict" }
            },
            RegistryReferenceNotFoundException missing => new ProblemDetails { Status = 404, Title = "Referenced registry record not found.", Extensions = { ["code"] = missing.Code } },
            RegistryForbiddenException => new ProblemDetails { Status = 403, Title = "Publisher application scope denied." },
            ContractPayloadTooLargeException => new ProblemDetails { Status = 413, Title = "Contract upload too large.", Detail = "Contract content is limited to 1 MiB and the JSON upload envelope to 2 MiB." },
            BadHttpRequestException { StatusCode: 400 } => HttpMethods.IsGet(httpContext.Request.Method)
                ? Validation("query", "Valid route and query parameter values are required.")
                : Validation("request", "A valid JSON request with all required fields is required."),
            BadHttpRequestException badRequest => new ProblemDetails
            {
                Status = badRequest.StatusCode,
                Title = "The request could not be read."
            },
            _ => null
        };

        if (problem is null)
        {
            return false;
        }

        await TypedResults.Problem(problem).ExecuteAsync(httpContext);
        return true;
    }

    private static ValidationProblemDetails Validation(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] })
        {
            Status = 400,
            Title = "Invalid registry request."
        };
}
