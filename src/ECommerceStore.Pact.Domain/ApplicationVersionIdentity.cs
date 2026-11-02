namespace ECommerceStore.Pact.Domain;

public sealed record ApplicationVersionIdentity
{
    public string ApplicationId { get; }
    public string CommitSha { get; }

    public ApplicationVersionIdentity(string? applicationId, string? commitSha)
    {
        ApplicationId = RegistryIdentity.ApplicationId(applicationId, "applicationId");
        CommitSha = RegistryIdentity.CommitSha(commitSha);
    }
}
