namespace ECommerceStore.Pact.Application;

public sealed class RegistryConflictException()
    : Exception("This application and commit are already registered with different immutable metadata.")
{ }
