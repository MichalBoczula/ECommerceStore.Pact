using System.Security.Cryptography;
using System.Text;

namespace ECommerceStore.Pact.Domain.Contracts;

public sealed record ContractProvenance
{
    public string SourceRef { get; }
    public string BuildUrl { get; }
    public string PublisherId { get; }
    public string IdentityHash => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{SourceRef}\n{BuildUrl}\n{PublisherId}")));

    public ContractProvenance(string? sourceRef, string? buildUrl, string? publisherId, string repositoryUrl)
    {
        var browserRepository = repositoryUrl.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? repositoryUrl[..^4] : repositoryUrl;
        if (sourceRef is null || sourceRef.Length > 256 ||
            !(sourceRef.StartsWith("refs/heads/", StringComparison.Ordinal) || sourceRef.StartsWith("refs/tags/", StringComparison.Ordinal) || sourceRef.StartsWith("refs/pull/", StringComparison.Ordinal)) ||
            sourceRef.EndsWith('/') || sourceRef.EndsWith('.') || sourceRef.Contains("..", StringComparison.Ordinal) ||
            sourceRef.Contains("//", StringComparison.Ordinal) || sourceRef.Contains("@{", StringComparison.Ordinal) ||
            sourceRef.Any(character => char.IsWhiteSpace(character) || char.IsControl(character) || "~^:?*[\\".Contains(character)))
            throw new RegistryValidationException("sourceRef", "A full refs/heads, refs/tags or refs/pull reference is required.");
        if (buildUrl is null || buildUrl.Length > 2048 || !Uri.TryCreate(buildUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            !uri.AbsoluteUri.StartsWith(browserRepository.TrimEnd('/') + "/", StringComparison.Ordinal))
            throw new RegistryValidationException("buildUrl", "Use an HTTPS build URL within the registered source repository, without credentials/query/fragment.");
        SourceRef = sourceRef;
        BuildUrl = uri.AbsoluteUri;
        PublisherId = RegistryIdentity.ApplicationId(publisherId, "publisherId");
    }
}
