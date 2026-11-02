namespace ECommerceStore.Pact.Application;

public sealed record RegistrationResult<T>(T Value, bool Created);
