namespace ECommerceStore.Pact.Api.Security;

public sealed class RegistryAuthOptions
{
    public Dictionary<string, PublisherCredential> Publishers { get; set; } = [];
}

public sealed class PublisherCredential
{
    public string TokenSha256 { get; set; } = "";
    public string[] ApplicationIds { get; set; } = [];
}
