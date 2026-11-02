namespace ECommerceStore.Pact.Application.Contracts;

public sealed class ContractConflictException() : Exception("This source version already has different contract content for the selected kind and integration.") { }
public sealed class RegistryReferenceNotFoundException(string code) : Exception("A referenced registry identity was not found.")
{
    public string Code { get; } = code;
}
public sealed class RegistryForbiddenException() : Exception("The publisher cannot write for this application.") { }
public sealed class ContractPayloadTooLargeException() : Exception("The contract upload exceeds the supported size limit.") { }
