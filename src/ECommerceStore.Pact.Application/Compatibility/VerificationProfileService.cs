using ECommerceStore.Pact.Application.Contracts;
using ECommerceStore.Pact.Domain;
using ECommerceStore.Pact.Domain.Compatibility;

namespace ECommerceStore.Pact.Application.Compatibility;

public sealed class VerificationProfileService(IIntegrationRepository integrations, IVerificationProfileRepository profiles, TimeProvider timeProvider)
{
    public async Task<RegistrationResult<VerificationProfile>> RegisterAsync(RegisterVerificationProfileCommand command, string publisherId, CancellationToken cancellationToken)
    {
        var integration = new IntegrationIdentity(command.ConsumerApplicationId, command.ProviderApplicationId);
        var candidate = VerificationProfile.Create(integration, command.Name, command.Version, command.Checks, publisherId, timeProvider.GetUtcNow());
        if (await integrations.GetAsync(integration, cancellationToken) is null) throw new RegistryReferenceNotFoundException("integration_not_found");
        var result = await profiles.RegisterAsync(candidate, cancellationToken);
        if (result.Value.ContentSha256 != candidate.ContentSha256) throw new VerificationProfileConflictException();
        return result;
    }
    public Task<VerificationProfile?> GetAsync(Guid id, CancellationToken cancellationToken) => profiles.GetAsync(id, cancellationToken);
}
