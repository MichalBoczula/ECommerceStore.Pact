using System.Text.Json.Nodes;

namespace ECommerceStore.Pact.Testing;

internal static class ComparisonSamples
{
    internal static JsonObject OpenApi() => JsonNode.Parse("""
        {"openapi":"3.0.3","info":{"title":"Orders","version":"1"},"paths":{"/orders":{"post":{
          "requestBody":{"required":true,"content":{"application/json":{"schema":{"type":"object","properties":{"customerId":{"type":"string"}}}}}},
          "responses":{"200":{"description":"Order","content":{"application/json":{"schema":{"type":"object","required":["totalAmount"],"properties":{"totalAmount":{"type":"number"}}}}}}}
        }}}}
        """)!.AsObject();
    internal static JsonObject Response(JsonObject document) => document["paths"]!["/orders"]!["post"]!["responses"]!["200"]!["content"]!["application/json"]!["schema"]!.AsObject();
    internal static JsonObject Request(JsonObject document) => document["paths"]!["/orders"]!["post"]!["requestBody"]!["content"]!["application/json"]!["schema"]!.AsObject();
}
