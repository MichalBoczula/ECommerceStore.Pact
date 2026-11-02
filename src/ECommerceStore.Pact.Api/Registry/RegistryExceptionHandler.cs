using ECommerceStore.Pact.Application;
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
            RegistryUnavailableException => new ProblemDetails { Status = 503, Title = "Registry storage unavailable." },
            BadHttpRequestException { StatusCode: 400 } => Validation("request", "A valid JSON request with all required fields is required."),
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
