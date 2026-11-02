namespace ECommerceStore.Pact.Domain;

public sealed class ApplicationVersion
{
    public Guid Id { get; }
    public ApplicationVersionIdentity Identity { get; }
    public string RepositoryUrl { get; }
    public string? ImageDigest { get; }
    public DateTimeOffset RegisteredAt { get; }

    private ApplicationVersion(Guid id, ApplicationVersionIdentity identity, string? repositoryUrl,
        string? imageDigest, DateTimeOffset registeredAt)
    {
        if (id == Guid.Empty)
        {
            throw new RegistryValidationException("id", "The application version ID cannot be empty.");
        }

        ArgumentNullException.ThrowIfNull(identity);
        var source = repositoryUrl?.Trim();
        if (source is null || source.Length > 2048 || !Uri.TryCreate(source, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host) ||
            uri.AbsolutePath == "/" || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
        {
            throw new RegistryValidationException("repositoryUrl",
                "An absolute HTTPS repository URL without credentials, query or fragment is required.");
        }

        var digest = imageDigest?.Trim().ToLowerInvariant();
        if (digest is not null && (digest.Length != 71 || !digest.StartsWith("sha256:", StringComparison.Ordinal) ||
            !digest[7..].All(char.IsAsciiHexDigit)))
        {
            throw new RegistryValidationException("imageDigest", "Use sha256: followed by 64 hexadecimal characters, or omit the digest.");
        }

        Id = id;
        Identity = identity;
        RepositoryUrl = uri.AbsoluteUri.TrimEnd('/');
        ImageDigest = digest;
        RegisteredAt = RegistryIdentity.Timestamp(registeredAt);
    }

    public static ApplicationVersion Register(ApplicationVersionIdentity identity, string? repositoryUrl,
        string? imageDigest, DateTimeOffset registeredAt) =>
        new(Guid.NewGuid(), identity, repositoryUrl, imageDigest, registeredAt);

    public static ApplicationVersion Rehydrate(Guid id, ApplicationVersionIdentity identity, string repositoryUrl,
        string? imageDigest, DateTimeOffset registeredAt) =>
        new(id, identity, repositoryUrl, imageDigest, registeredAt);

    public bool HasSameMetadata(ApplicationVersion other) =>
        Identity == other.Identity && RepositoryUrl == other.RepositoryUrl && ImageDigest == other.ImageDigest;
}
