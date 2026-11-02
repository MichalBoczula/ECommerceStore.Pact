using System.Security.Claims;
using ECommerceStore.Pact.Application.Contracts;
using ECommerceStore.Pact.Domain;

namespace ECommerceStore.Pact.Api.Security;

public static class PublisherAccess
{
    public static void RequireApplication(ClaimsPrincipal principal, string? applicationId)
    {
        var normalized = RegistryIdentity.ApplicationId(applicationId, "applicationId");
        if (!principal.FindAll("registry:application").Any(claim => claim.Value == "*" || claim.Value == normalized))
            throw new RegistryForbiddenException();
    }

    public static string PublisherId(ClaimsPrincipal principal) => principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new RegistryForbiddenException();
}
