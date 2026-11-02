namespace ECommerceStore.Pact.Infrastructure.Mongo;

public sealed class MongoOptions
{
    public const string SectionName = "Mongo";

    public string ConnectionString { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = "ecommercestore-pact";
    public int TimeoutSeconds { get; set; } = 3;
}
