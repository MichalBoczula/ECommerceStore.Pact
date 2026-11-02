using System.Security.Cryptography;

namespace ECommerceStore.Pact.Domain.Contracts;

public sealed class ContractArtifact
{
    public const int MaxContentBytes = 1024 * 1024;
    private readonly byte[] _content;
    public string Sha256 { get; }
    public ContractKind Kind { get; }
    public string SpecificationVersion { get; }
    public int Length => _content.Length;
    public DateTimeOffset StoredAt { get; }

    private ContractArtifact(ContractKind kind, string specificationVersion, byte[] content, DateTimeOffset storedAt)
    {
        if (!Enum.IsDefined(kind) || string.IsNullOrWhiteSpace(specificationVersion) || specificationVersion.Length > 32)
            throw new RegistryValidationException("kind", "A supported contract kind and specification version are required.");
        if (content.Length is 0 or > MaxContentBytes)
            throw new RegistryValidationException("contentBase64", "Contract content must contain 1 to 1048576 bytes.");
        _content = (byte[])content.Clone();
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(_content));
        Kind = kind;
        SpecificationVersion = specificationVersion;
        StoredAt = RegistryIdentity.Timestamp(storedAt);
    }

    public byte[] GetContent() => (byte[])_content.Clone();

    public static ContractArtifact Create(ContractKind kind, string specificationVersion, byte[] content, DateTimeOffset now) =>
        new(kind, specificationVersion, content, now);

    public static ContractArtifact Rehydrate(string expectedHash, ContractKind kind, string specificationVersion,
        byte[] content, DateTimeOffset storedAt)
    {
        var artifact = new ContractArtifact(kind, specificationVersion, content, storedAt);
        if (artifact.Sha256 != NormalizeHash(expectedHash))
            throw new InvalidOperationException("Stored contract content does not match its hash.");
        return artifact;
    }

    public static string NormalizeHash(string? hash)
    {
        var normalized = hash?.Trim().ToLowerInvariant();
        if (normalized is null || normalized.Length != 64 || !normalized.All(char.IsAsciiHexDigit))
            throw new RegistryValidationException("sha256", "Use a 64-character hexadecimal SHA-256 hash.");
        return normalized;
    }
}
