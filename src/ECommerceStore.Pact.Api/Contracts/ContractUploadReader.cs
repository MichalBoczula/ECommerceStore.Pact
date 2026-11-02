using ECommerceStore.Pact.Api.Registry;
using ECommerceStore.Pact.Application.Contracts;

namespace ECommerceStore.Pact.Api.Contracts;

internal static class ContractUploadReader
{
    internal const int MaxRequestBytes = 2 * 1024 * 1024;

    internal static Task<PublishContractRequest> ReadAsync(HttpRequest request, CancellationToken cancellationToken) =>
        BoundedJsonRequestReader.ReadAsync<PublishContractRequest>(request, MaxRequestBytes, () => new ContractPayloadTooLargeException(), cancellationToken);
}
