namespace ECommerceStore.Pact.Domain;

internal static class RegistryIdentity
{
    internal static string ApplicationId(string? value, string field)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > 100 ||
            normalized[0] == '-' || normalized[^1] == '-' || normalized.Contains("--", StringComparison.Ordinal) ||
            normalized.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            throw new RegistryValidationException(field,
                "Use 1 to 100 ASCII letters/digits separated by single hyphens.");
        }

        return normalized;
    }

    internal static string CommitSha(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (normalized is null || normalized.Length is not (40 or 64) || !normalized.All(char.IsAsciiHexDigit))
        {
            throw new RegistryValidationException("commitSha", "A full 40- or 64-character hexadecimal commit SHA is required.");
        }

        return normalized;
    }

    internal static DateTimeOffset Timestamp(DateTimeOffset value) =>
        DateTimeOffset.FromUnixTimeMilliseconds(value.ToUnixTimeMilliseconds());
}
