namespace ECommerceStore.Pact.Application;

public sealed class RegistryUnavailableException(Exception innerException)
    : Exception("Registry storage is unavailable.", innerException)
{ }
