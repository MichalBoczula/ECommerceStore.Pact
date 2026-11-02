using ECommerceStore.Pact.Domain;

namespace ECommerceStore.Pact.Application;

public sealed class ApplicationVersionService(IApplicationVersionRepository repository, TimeProvider timeProvider)
{
    public async Task<RegistrationResult<ApplicationVersion>> RegisterAsync(string? applicationId, string? commitSha,
        string? repositoryUrl, string? imageDigest, CancellationToken cancellationToken)
    {
        var candidate = ApplicationVersion.Register(new ApplicationVersionIdentity(applicationId, commitSha),
            repositoryUrl, imageDigest, timeProvider.GetUtcNow());
        var result = await repository.RegisterAsync(candidate, cancellationToken);
        if (!candidate.HasSameMetadata(result.Value))
        {
            throw new RegistryConflictException();
        }

        return result;
    }

    public Task<ApplicationVersion?> GetAsync(string? applicationId, string? commitSha, CancellationToken cancellationToken) =>
        repository.GetAsync(new ApplicationVersionIdentity(applicationId, commitSha), cancellationToken);
}
