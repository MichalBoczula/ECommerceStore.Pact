# PACT/10 integration coverage

Snapshot from 12 November 2026. The live registry has passing provider evidence
for all nine planned integration pairs. The records below cover 30 Pact
interactions across 15 consumer operations. The source audit found 19 additional
operations with a Pact coverage gap.

Together, the linked publication and run identify the exact consumer commit, provider
commit, contract hash and CI execution. The same HTTP route can carry different
requirements for different consumers. Storefront routes shown here omit the
`/backend` proxy prefix. BFF routing is exercised separately by browser
acceptance. The Web → BFF Pact uses the shared registration client from that
suite.

## Verified operations

The provider column names the application that owns the HTTP behavior. Each row
lists the operation and the requirements exercised by the linked consumer test.

| Consumer → provider | Operation | Consumer requirement exercised | Consumer test and publication | Provider replay |
| --- | --- | --- | --- | --- |
| Payments → Invoice | `GET /orders/{orderId}` | Created order ID, amount, currency and status in PLN, JPY, KWD and USD. Paid order includes client ownership. Missing order returns 404. | [Consumer test][payments-test] · [Pact][payments-invoice-pact] | [Invoice CI][payments-invoice-run] |
| Payments → Invoice | `PATCH /orders/{orderId}/status` | Sends the Paid transition for a Created order and receives HTTP 200. | [Consumer test][payments-test] · [Pact][payments-invoice-pact] | [Invoice CI][payments-invoice-run] |
| Payments → Invoice | `GET /invoices/by-order/{orderId}` | Completed invoice identity, order, billing version, storage reference and creation time. Missing invoice returns 404. | [Consumer test][payments-test] · [Pact][payments-invoice-pact] | [Invoice CI][payments-invoice-run] |
| Payments → Invoice | `POST /invoices/{clientId}/{orderId}` | HTTP 200 with the completed invoice for a Paid order and its owner. | [Consumer test][payments-test] · [Pact][payments-invoice-pact] | [Invoice CI][payments-invoice-run] |
| Invoice → Products | `POST /mobile-phones/by-ids` | Product ID, name, brand, price and currency for checkout snapshots. A mixed existing/missing batch returns 404. | [Consumer test][invoice-products-test] · [Pact][invoice-products-pact] | [Products CI][invoice-products-run] |
| Web → Products | `POST /mobile-phones/by-ids` | Product ID, name, brand, photo, price, currency and display details for cart and favorites. A mixed existing/missing batch returns 404. | [Consumer test][web-products-test] · [Pact][web-products-pact] | [Products CI][web-products-run] |
| Web → Users | `GET /customers/external/{externalId}` | Customer ID, external ID, update timestamp, individual and company details, and billing and shipping addresses. Missing profile returns 404. | [Consumer test][web-users-test] · [Pact][web-users-pact] | [Users CI][web-users-run] |
| Web → Invoice | `GET /shopping-carts/client/{clientId}` | Cart and client IDs, timestamps, product IDs and quantities. Missing cart returns 404. | [Consumer test][web-invoice-test] · [Pact][web-invoice-pact] | [Invoice CI][web-invoice-run] |
| Web → Payments | `GET /payments/order/{orderId}` | Payment and order IDs, status, exact minor-unit amount and currency. Missing payment returns 404. | [Consumer test][web-payments-test] · [Pact][web-payments-pact] | [Payments CI][web-payments-run] |
| Web → BFF | `POST /registrations/customers` | Sends external ID and profile details. Returns a customer GUID after registration and cart confirmation. | [Consumer test][web-bff-test] · [Pact][web-bff-pact] | [BFF CI][web-bff-run] |
| Web → BFF | `POST /registrations/customers/{externalId}/cart` | Repairs a missing cart for the existing profile and returns its customer GUID. | [Consumer test][web-bff-test] · [Pact][web-bff-pact] | [BFF CI][web-bff-run] |
| BFF → Users | `GET /customers/external/{externalId}` | Customer ID and profile for a registration retry, or 404 for a missing customer. | [Consumer test][bff-users-test] · [Pact][bff-users-pact] | [Users CI][bff-users-run] |
| BFF → Users | `POST /customers` | Creates a profile with the supplied individual details and an empty company list. Duplicate registration returns 409 so the BFF can look it up again. | [Consumer test][bff-users-test] · [Pact][bff-users-pact] | [Users CI][bff-users-run] |
| BFF → Invoice | `GET /shopping-carts/client/{clientId}` | Cart ID, matching client ID and stored product line, or 404 for a missing cart. | [Consumer test][bff-invoice-test] · [Pact][bff-invoice-pact] | [Invoice CI][bff-invoice-run] |
| BFF → Invoice | `POST /shopping-carts/{clientId}` | Bodyless create returns a new cart ID, matching client ID and empty lines. Duplicate create returns 409 so the BFF can look it up again. | [Consumer test][bff-invoice-test] · [Pact][bff-invoice-pact] | [Invoice CI][bff-invoice-run] |

## Operations with a Pact coverage gap

The audited storefront source calls the following operations. The registry
evidence above contains no consumer Pact interaction or provider replay for
these specific consumer operations. Other test suites can cover the same routes.

| Consumer → provider | Operation | Consumer use |
| --- | --- | --- |
| Web → Products | `GET /mobile-phones?amount={amount}` | Catalog listing. |
| Web → Products | `GET /mobile-phones/{id}` | Product detail and gallery. |
| Web → Products | `GET /mobile-phones/top` | Home page top products. |
| Web → Products | `POST /mobile-phones/filter` | Filtered catalog results. |
| Web → Users | `PUT /customers/{customerId}/individual` | Save individual profile changes. |
| Web → Users | `PUT /customers/{customerId}/companies/{companyId}` | Save company profile changes. |
| Web → Users | `GET /favorites/clients/{clientId}` | Read saved favorites. |
| Web → Users | `POST /favorites/clients/{clientId}` | Add a favorite product. |
| Web → Users | `DELETE /favorites/clients/{clientId}/products/{productId}` | Remove one favorite. |
| Web → Users | `DELETE /favorites/clients/{clientId}` | Clear favorites. |
| Web → Invoice | `PUT /shopping-carts/{clientId}` | Update cart product IDs and quantities. |
| Web → Invoice | `POST /orders/client/{clientId}` | Create an order from the cart. |
| Web → Invoice | `GET /orders/client/{clientId}` | Load customer order history. |
| Web → Invoice | `GET /orders/{orderId}` | Load order details and confirm payment ownership. |
| Web → Invoice | `GET /invoices/by-order/{orderId}` | Check whether the order has a completed invoice. |
| Web → Invoice | `GET /client-data-versions/client/{clientId}` | Load the current billing snapshot. |
| Web → Invoice | `POST /client-data-versions/{clientId}` | Save a billing snapshot before Checkout. |
| Web → Payments | `POST /payments/{orderId}/pay` | Prepare the order payment. |
| Web → Payments | `POST /payments/{orderId}/checkout` | Open or resume hosted Checkout. |

Source snapshots used for the inventory include the [storefront adapters][web-source],
[BFF registration gateways][bff-source], [Invoice checkout adapter][invoice-source],
and [Payments order and fulfillment adapters][payments-source].

[payments-test]: https://github.com/MichalBoczula/ECommerceStorePayments/blob/4b8e375dbc5a0092b2ec88c0fd8a51ee59fdcee8/tests/unit/external_providers/test_orders_pact.py
[invoice-products-test]: https://github.com/MichalBoczula/ECommerceStoreInvoice/blob/03169c98d64f630dd6ab2897a183f44e62557f71/tests/ECommerceStoreInvoice.ExternalProviders.IntegrationTests/Tests/ProductsConsumerPactTests.cs
[web-products-test]: https://github.com/MichalBoczula/ecommerce-store-web/blob/d8837ccf6257c389f6f9ee4c249aa1dc7cd67c12/tests/pact/products-lookup.test.ts
[web-users-test]: https://github.com/MichalBoczula/ecommerce-store-web/blob/b52edaddd7fb2d3dd3f1015cca495cc9c6835938/tests/pact/customer-profile.test.ts
[web-invoice-test]: https://github.com/MichalBoczula/ecommerce-store-web/blob/e9510e8d80dc000fcab02b01036bd409681c2933/tests/pact/shopping-cart-lookup.test.ts
[web-payments-test]: https://github.com/MichalBoczula/ecommerce-store-web/blob/d07c41e520b1182d95b53e11fd0d21e70cbfd705/tests/pact/payment-lookup.test.ts
[web-bff-test]: https://github.com/MichalBoczula/ecommerce-store-web/blob/5317a08a0b14649122ddb52308e13352f6a38ef8/tests/pact/bff-registration.test.ts
[bff-users-test]: https://github.com/MichalBoczula/ECommerceStoreBFF/blob/b2bf777ee0ab5704805e838f7db81983187bce83/test/ECommerceStoreBFF.IntegrationTests/Features/Users/UsersConsumerPactTests.cs
[bff-invoice-test]: https://github.com/MichalBoczula/ECommerceStoreBFF/blob/ff0a0805d41e26cfd346910a99e50d4c6d1ff6ae/test/ECommerceStoreBFF.IntegrationTests/Features/Orders/InvoiceConsumerPactTests.cs
[payments-invoice-pact]: https://ca-ecommercestore-pact-dev.wittyflower-559cf339.francecentral.azurecontainerapps.io/api/contract-publications/02a4fea3-daba-453e-bef2-68b108af8998
[invoice-products-pact]: https://ca-ecommercestore-pact-dev.wittyflower-559cf339.francecentral.azurecontainerapps.io/api/contract-publications/b239f945-4bea-4cd2-a3a2-e6b913d2a980
[web-products-pact]: https://ca-ecommercestore-pact-dev.wittyflower-559cf339.francecentral.azurecontainerapps.io/api/contract-publications/dec6cc6c-ba8f-4fe6-9bf0-0297cd2a4036
[web-users-pact]: https://ca-ecommercestore-pact-dev.wittyflower-559cf339.francecentral.azurecontainerapps.io/api/contract-publications/eaa46014-4200-4b49-822d-b8bb9f36973c
[web-invoice-pact]: https://ca-ecommercestore-pact-dev.wittyflower-559cf339.francecentral.azurecontainerapps.io/api/contract-publications/578cf2e1-bd08-47af-b59a-3c8696130580
[web-payments-pact]: https://ca-ecommercestore-pact-dev.wittyflower-559cf339.francecentral.azurecontainerapps.io/api/contract-publications/d615886f-ddc6-4119-8326-e382fd03cd1b
[web-bff-pact]: https://ca-ecommercestore-pact-dev.wittyflower-559cf339.francecentral.azurecontainerapps.io/api/contract-publications/876f5d56-c0ab-465c-ade9-3e6d2addcb63
[bff-users-pact]: https://ca-ecommercestore-pact-dev.wittyflower-559cf339.francecentral.azurecontainerapps.io/api/contract-publications/be3b1a5c-8215-4fa5-85ee-75aef584239a
[bff-invoice-pact]: https://ca-ecommercestore-pact-dev.wittyflower-559cf339.francecentral.azurecontainerapps.io/api/contract-publications/c9a57254-8d57-4855-be0d-8c68bb243f42
[payments-invoice-run]: https://ca-ecommercestore-pact-dev.wittyflower-559cf339.francecentral.azurecontainerapps.io/api/verification-runs/13b5f3c0-e1b0-4cb9-bf55-7f729925650e
[invoice-products-run]: https://ca-ecommercestore-pact-dev.wittyflower-559cf339.francecentral.azurecontainerapps.io/api/verification-runs/d8062086-159d-43e0-9844-9caa82619ef5
[web-products-run]: https://ca-ecommercestore-pact-dev.wittyflower-559cf339.francecentral.azurecontainerapps.io/api/verification-runs/e281b27c-89b8-4eb0-8ade-a18525e9d3c6
[web-users-run]: https://ca-ecommercestore-pact-dev.wittyflower-559cf339.francecentral.azurecontainerapps.io/api/verification-runs/d9c6d2c6-e8a8-420e-8283-0d8f587c722f
[web-invoice-run]: https://ca-ecommercestore-pact-dev.wittyflower-559cf339.francecentral.azurecontainerapps.io/api/verification-runs/4f6bdf70-d901-450a-8d62-f6db7a450c7d
[web-payments-run]: https://ca-ecommercestore-pact-dev.wittyflower-559cf339.francecentral.azurecontainerapps.io/api/verification-runs/f3b3c532-8270-4701-968d-6fc48eb7acd8
[web-bff-run]: https://ca-ecommercestore-pact-dev.wittyflower-559cf339.francecentral.azurecontainerapps.io/api/verification-runs/c2ab4424-d5a8-48f8-bf5d-f4b9c24eeb30
[bff-users-run]: https://ca-ecommercestore-pact-dev.wittyflower-559cf339.francecentral.azurecontainerapps.io/api/verification-runs/9f1cc9f2-b11c-4e1e-b445-cc43ea6400cb
[bff-invoice-run]: https://ca-ecommercestore-pact-dev.wittyflower-559cf339.francecentral.azurecontainerapps.io/api/verification-runs/84048629-63d3-4c4c-9ea7-a3474b01b49e
[web-source]: https://github.com/MichalBoczula/ecommerce-store-web/tree/5317a08a0b14649122ddb52308e13352f6a38ef8/src/app/features
[bff-source]: https://github.com/MichalBoczula/ECommerceStoreBFF/blob/ff0a0805d41e26cfd346910a99e50d4c6d1ff6ae/src/ECommerceStoreBFF.Infrastructure/Registration/RegistrationGateways.cs
[invoice-source]: https://github.com/MichalBoczula/ECommerceStoreInvoice/tree/41f5f0b9976ecfd87916de3a568515da94de7c12/src
[payments-source]: https://github.com/MichalBoczula/ECommerceStorePayments/tree/4b8e375dbc5a0092b2ec88c0fd8a51ee59fdcee8/src/ecommerce_store_payments/infrastructure/clients/orders
