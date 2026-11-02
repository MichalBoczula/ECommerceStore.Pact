namespace ECommerceStore.Pact.Domain.Verification;

public sealed record VerificationIssue
{
    public VerificationIssueKind Kind { get; }
    public string Code { get; }
    public string Message { get; }
    public string? Interaction { get; }
    public string? Method { get; }
    public string? Path { get; }
    public string? JsonPath { get; }
    public string? ExpectedJson { get; }
    public bool? ActualPresent { get; }
    public string? ActualJson { get; }
    public string? EvidenceUrl { get; }

    public VerificationIssue(VerificationIssueKind kind, string? code, string? message, string? interaction = null,
        string? method = null, string? path = null, string? jsonPath = null, string? expectedJson = null,
        bool? actualPresent = null, string? actualJson = null, string? evidenceUrl = null)
    {
        if (!Enum.IsDefined(kind)) throw new RegistryValidationException("issues.kind", "Use Mismatch or ExecutionError.");
        Kind = kind;
        Code = VerificationValues.Text(code, "issues.code", 100);
        Message = VerificationValues.Text(message, "issues.message", 4096, multiline: true);
        Interaction = interaction is null ? null : VerificationValues.Text(interaction, "issues.interaction", 512);
        Method = method is null ? null : VerificationValues.Text(method, "issues.method", 20).ToUpperInvariant();
        Path = path is null ? null : VerificationValues.Text(path, "issues.path", 2048);
        JsonPath = jsonPath is null ? null : VerificationValues.Text(jsonPath, "issues.jsonPath", 1024);
        if ((Method is null) != (Path is null) || Method?.Any(c => !char.IsAsciiLetter(c)) == true || (Path is not null && !Path.StartsWith('/')))
            throw new RegistryValidationException("issues.operation", "Supply both an HTTP method and a path starting with /.");
        ExpectedJson = VerificationValues.Json(expectedJson, "issues.expected");
        ActualJson = VerificationValues.Json(actualJson, "issues.actual");
        ActualPresent = actualPresent;
        if ((actualPresent == true && ActualJson is null) || (actualPresent != true && ActualJson is not null))
            throw new RegistryValidationException("issues.actual", "Supply an actual JSON value only when actualPresent is true; JSON null is a present value.");
        if (kind == VerificationIssueKind.Mismatch && (Interaction is null || Method is null || JsonPath is null || ExpectedJson is null || actualPresent is null))
            throw new RegistryValidationException("issues", "A mismatch requires interaction, operation, JSON path, expected value and actualPresent.");
        EvidenceUrl = evidenceUrl is null ? null : VerificationValues.EvidenceUrl(evidenceUrl);
    }
}
