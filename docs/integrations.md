# Integration inventory

Source inspection baseline: 2026-11-02. These are planned coverage edges; no row in this
inventory claims that Pact tests are already implemented.

| Consumer | Provider / transport | Production boundary | Planned scope |
| --- | --- | --- | --- |
| Payments | Invoice | HttpOrderReader | GET /orders/{orderId}; first pilot. |
| Payments | Invoice | HttpFulfillmentOrders, HttpFulfillmentInvoices | Order status and invoice lookup/creation. |
| Invoice | ProductsCatalog | ExternalProductServiceClient, ProductVersionMapping | POST /mobile-phones/by-ids and snapshot mapping. |
| Web | ProductsCatalog via BFF | MobilePhonesKiotaRepository and mappers | List, details, IDs, filter, top products. |
| Web | Users via BFF | CustomerProfileKiotaRepository, FavoritesKiotaRepository | Profiles, updates and favorites. |
| Web | Invoice via BFF | OrdersKiotaRepository, OrderHistoryKiotaRepository, billing adapter | Cart, checkout, order history and invoice reads. |
| Web | Payments via BFF | PaymentsKiotaRepository, PaymentProgressKiotaRepository | Payment initiation, checkout and progress. |
| Web | BFF | Registration adapter | BFF-owned registration and cart repair operations. |
| BFF | Users | CustomerRegistrationGateway | Customer lookup/creation during registration. |
| BFF | Invoice | ShoppingCartRegistrationGateway | Cart lookup/creation during registration. |

Repository sources:

- [Payments](https://github.com/MichalBoczula/ECommerceStorePayments/tree/571c29d67826844c294998059840eebc55c3d826)
- [Invoice](https://github.com/MichalBoczula/ECommerceStoreInvoice/tree/d28dd23c14dee519ac753da6fe2c32289581ca3e)
- [Web](https://github.com/MichalBoczula/ecommerce-store-web/tree/a5fe2f3dd0e9eb46be86df8109638c2fccad4277)
- [BFF](https://github.com/MichalBoczula/ECommerceStoreBFF/tree/3bbbe1c1eb7e76fc60b1bfeebeba613c9477ad7b)

Transparent YARP routes reuse upstream business contracts. BFF-owned orchestration has
its own public contract and downstream consumer requirements. Routing smoke tests cover
the proxy boundary; provider verification targets the application owning the behavior.
Before implementing an edge, re-read the relevant current source and list its actual
used operations; a generated Kiota client can include unused operations.
