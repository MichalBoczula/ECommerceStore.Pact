# PACT/08: Payments consumer pilot

Implementation: [Payments PR #49](https://github.com/MichalBoczula/ECommerceStorePayments/pull/49).
The consumer code/test/CI live in Payments; the registry stores their later publications and
provider verification evidence. No registry deployment is required for this consumer task.

## Executed boundary

`HttpOrderReader -> generated OrdersClient -> OrdersRequestAdapter -> HTTPX -> Pact HTTP`
uses `PreciseOrderJsonFactory` on real response bytes and the production mapper/minor-unit
converter on the resulting Kiota DTO. Expected application results assert the requested UUID,
Created status, exact integer minor units and currency. There is no substitute HTTP transport,
handwritten test client or provider-generated expectation.

Logical party names are `payments` and `invoice`, matching registry IDs. Official Pact Python
4.2.0 and its native FFI are locked as development dependencies. The file is Pact specification
4.0 Synchronous/HTTP. The consumer test repeats the complete set on two fresh servers and
requires identical official-writer bytes before exporting an artifact.

| Interaction | Wire total / currency | Two line amounts | Expected application money |
| --- | --- | --- | --- |
| Created PLN order | 0.29 PLN | 0.10 + 0.19 | 29 PLN minor units |
| Created JPY order | 500 JPY | 200 + 300 | 500 JPY minor units |
| Created KWD order | 1.234 KWD | 1 + 0.234 | 1234 KWD minor units |
| Created USD order | 12345.67 USD | 12300 + 45.67 | 1234567 USD minor units |
| Missing order | HTTP 404 problem+json | Not applicable | OrderNotFoundError |

Successful obligations contain only `id`, `status`, `totalAmount`, `totalCurrency`,
`lines[].lineTotalAmount` and `lines[].productVersion.priceCurrency`. The minimal response
omits provider-only client/date/product metadata. Fixed values deliberately keep the requested
ID, line sum, currency and status coherent in the provider states. Provider verification must
use deterministic seed data for these examples; arbitrary IDs/totals are not interchangeable.
The missing-order problem only requires a parseable object containing status 404; error text
and trace identifiers do not become consumer requirements.

Missing consumed-field, malformed-response, timeout, unavailable-service and very-large-decimal
regressions remain local boundary tests. Pact FFI can round very large decimal examples in its
JSON representation; the existing 29-digit parser regression continues to use exact local wire
bytes. No invalid response is exported as an obligation for Invoice to produce.

## Reproduce and retrieve

In the Payments checkout, with its pinned Python/uv installed:

```bash
bash scripts/ci.sh sync
bash scripts/ci.sh pact
```

No Docker, MongoDB, Invoice, registry URL or publisher token is needed. The focused command
clears stale output and produces:

- `artifacts/verification/pact/pacts/payments-invoice.json`: unmodified official Pact output.
- `artifacts/verification/pact/pacts/manifest.json`: SHA-256, specification/tool versions and
  five-interaction count.
- `artifacts/verification/pact/junit.xml`: focused execution report.

The existing required ExternalProviders job also executes this test and uploads
`payments-invoice-pact` from its own `artifacts/verification/externalproviders/pacts/`
directory. Infrastructure executes it too, under its isolated suite directory, preserving
existing coverage/baseline requirements. Downloads must come from the successful run for the
exact consumer revision. Source commit and content hash are separate identities: unchanged
consumer requirements may produce the same file in several commits.

## Provider handoff (PACT/09, pending)

Implement isolated, parameterized provider states on a candidate Invoice API over TCP:

- `a Created order exists for payment`: `orderId`, `currency`, decimal-string `totalAmount`
  and decimal-string array `lineAmounts`; seed valid order/product snapshots with matching data.
- `the order does not exist`: `orderId`; ensure the isolated state has no matching order.

Verify this exact artifact using official Pact tooling, retain candidate/source identities and
structured results, then publish immutable contracts and exact-version evidence to the registry.
Provider CI must prove that removing a consumed field fails and restoring it passes. Uploads,
provider verification, compatibility decisions and cloud readiness are not established by this
consumer-only implementation. Payments fulfillment operations remain future integration scope.
