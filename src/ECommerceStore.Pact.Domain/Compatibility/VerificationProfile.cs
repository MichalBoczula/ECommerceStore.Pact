using ECommerceStore.Pact.Domain.Contracts;
using ECommerceStore.Pact.Domain.Verification;

namespace ECommerceStore.Pact.Domain.Compatibility;

public sealed record RequiredVerificationCheck
{
    public string CheckId { get; }
    public string VerifierName { get; }
    public string VerifierVersion { get; }
    public string ConfigurationSha256 { get; }
    public string Fingerprint => VerificationValues.Hash(CheckId, VerifierName, VerifierVersion, ConfigurationSha256);
    public RequiredVerificationCheck(string? checkId, string? verifierName, string? verifierVersion, string configurationSha256)
    {
        CheckId = VerificationValues.Text(checkId, "checkId", 200);
        VerifierName = VerificationValues.Text(verifierName, "verifierName", 100);
        VerifierVersion = VerificationValues.Text(verifierVersion, "verifierVersion", 100);
        ConfigurationSha256 = ContractArtifact.NormalizeHash(configurationSha256);
    }
}

public sealed class VerificationProfile
{
    public Guid Id { get; }
    public IntegrationIdentity Integration { get; }
    public string Name { get; }
    public string Version { get; }
    public IReadOnlyList<RequiredVerificationCheck> Checks { get; }
    public string PublisherId { get; }
    public DateTimeOffset RegisteredAt { get; }
    public string Key => VerificationValues.Hash("verification-profile-v1", Integration.ConsumerApplicationId, Integration.ProviderApplicationId, Name, Version);
    public string ContentSha256 => VerificationValues.Hash("verification-profile-content-v1", Checks.Select(check => check.Fingerprint).ToArray());
    private VerificationProfile(Guid id, IntegrationIdentity integration, string name, string version, IEnumerable<RequiredVerificationCheck> checks,
        string publisherId, DateTimeOffset registeredAt)
    {
        var values = checks.Take(11).ToArray();
        if (id == Guid.Empty || values.Length is < 1 or > 10 || values.Any(value => value is null) ||
            values.Select(value => value.CheckId).Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new RegistryValidationException("checks", "Provide 1 to 10 required checks with distinct check IDs.");
        Id = id; Integration = integration; Name = RegistryIdentity.ApplicationId(name, "name");
        Version = VerificationValues.Text(version, "version", 100); Checks = Array.AsReadOnly(values.OrderBy(check => check.CheckId, StringComparer.Ordinal).ToArray());
        PublisherId = RegistryIdentity.ApplicationId(publisherId, "publisherId"); RegisteredAt = RegistryIdentity.Timestamp(registeredAt);
    }
    public static VerificationProfile Create(IntegrationIdentity integration, string name, string version, IEnumerable<RequiredVerificationCheck> checks,
        string publisherId, DateTimeOffset now) => new(Guid.NewGuid(), integration, name, version, checks, publisherId, now);
    public static VerificationProfile Rehydrate(Guid id, IntegrationIdentity integration, string name, string version, IEnumerable<RequiredVerificationCheck> checks,
        string publisherId, DateTimeOffset now) => new(id, integration, name, version, checks, publisherId, now);
}
