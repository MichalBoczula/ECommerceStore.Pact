using ECommerceStore.Pact.Domain;
using Microsoft.Extensions.Options;

namespace ECommerceStore.Pact.Api.Security;

public sealed class RegistryAuthValidator : IValidateOptions<RegistryAuthOptions>
{
    public ValidateOptionsResult Validate(string? name, RegistryAuthOptions options)
    {
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, publisher) in options.Publishers)
        {
            try
            {
                _ = RegistryIdentity.ApplicationId(id, "publisherId");
                foreach (var application in publisher.ApplicationIds)
                    if (application != "*") _ = RegistryIdentity.ApplicationId(application, "applicationId");
            }
            catch (RegistryValidationException) { return ValidateOptionsResult.Fail("RegistryAuth publisher/application IDs must be valid registry IDs."); }
            if (publisher.TokenSha256.Length != 64 || !publisher.TokenSha256.All(char.IsAsciiHexDigit) ||
                !hashes.Add(publisher.TokenSha256) || publisher.ApplicationIds.Length == 0)
                return ValidateOptionsResult.Fail("RegistryAuth publishers require unique SHA-256 token hashes and nonempty application scopes.");
        }
        // No credentials means no writes are permitted; public reads and health still work.
        return ValidateOptionsResult.Success;
    }
}
