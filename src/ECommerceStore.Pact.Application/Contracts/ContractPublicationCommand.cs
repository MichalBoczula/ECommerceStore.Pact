namespace ECommerceStore.Pact.Application.Contracts;

public sealed record ContractPublicationCommand(string SourceApplicationId, string CommitSha, string Kind,
    string? ConsumerApplicationId, string? ProviderApplicationId, string SourceRef, string BuildUrl, string ContentBase64);
