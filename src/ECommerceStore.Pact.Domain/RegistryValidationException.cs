namespace ECommerceStore.Pact.Domain;

public sealed class RegistryValidationException(string field, string message)
    : Exception("The request contains invalid registry data.")
{
    public string Field { get; } = field;
    public string ValidationMessage { get; } = message;
}
