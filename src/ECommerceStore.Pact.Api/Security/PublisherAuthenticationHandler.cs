using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using ECommerceStore.Pact.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ECommerceStore.Pact.Api.Security;

public sealed class PublisherAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, IOptions<RegistryAuthOptions> publishers)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "RegistryPublisher";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization;
        if (header.Count == 0) return Task.FromResult(AuthenticateResult.NoResult());
        if (header.Count != 1 || !header.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.Fail("Invalid publisher credential."));
        var token = header.ToString()[7..];
        if (token.Length is < 32 or > 512 || token.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
            return Task.FromResult(AuthenticateResult.Fail("Invalid publisher credential."));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        foreach (var (id, credential) in publishers.Value.Publishers)
        {
            if (!CryptographicOperations.FixedTimeEquals(hash, Convert.FromHexString(credential.TokenSha256))) continue;
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, RegistryIdentity.ApplicationId(id, "publisherId")) };
            claims.AddRange(credential.ApplicationIds.Select(application => new Claim("registry:application",
                application == "*" ? "*" : RegistryIdentity.ApplicationId(application, "applicationId"))));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
        return Task.FromResult(AuthenticateResult.Fail("Invalid publisher credential."));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Bearer";
        await TypedResults.Problem(statusCode: 401, title: "Publisher authentication required.").ExecuteAsync(Context);
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        TypedResults.Problem(statusCode: 403, title: "Publisher access denied.").ExecuteAsync(Context);
}
