# Withdrawal Provider API Integration Plan

This plan defines the minimum provider API surface and integration steps needed to implement a withdrawal-focused provider integration in a BTCPay Server plugin.

## External API surface used by the plugin
The plugin depends on these external API operations:

- `POST /api/v0/application/btcpay/signup-url` (optional onboarding link)
- `GET /api/v0/user/user-id` (API key validation)
- `POST /api/v0/offramp/rates` (BTC/fiat conversion rate)
- `POST /api/v0/offramp/order` (create withdrawal order and return payout destination)
- `POST /api/v0/user/get-balance/fiat` (dashboard fiat balance)
- `POST /api/v0/account/transactions` (recent transaction history)

## Endpoint contract plan

### Tier 1 — Required for core withdrawal automation (MVP)
1. **Health/Auth check**
   - **Endpoint:** `GET /api/v1/user/user-id` (or equivalent)
   - **Purpose:** Validate API key at save/test time.
   - **Response:** `{ "userId": "string" }`

2. **Rate quote**
   - **Endpoint:** `POST /api/v1/offramp/rates`
   - **Request:** `{ "ticker": "BTCEUR" }`
   - **Response (minimum):**
     - `ticker`
     - `currency`
     - `price` (optional generic market price)
     - `providerPrice` (effective quote used for threshold/minimum checks)
   - **Purpose:** Convert configured fiat thresholds/minimums into BTC for order gating.

3. **Create withdrawal order**
   - **Endpoint:** `POST /api/v1/offramp/order`
   - **Request:**
     - `sourceAmount` (sats)
     - `ipAddress` (string)
     - `paymentMethod` (`LIGHTNING` / `ON_CHAIN`)
   - **Response:**
     - `id` (provider order id)
     - `amount` (sats accepted)
     - `invoice` (BOLT11, nullable)
     - `depositAddress` (on-chain address, nullable)
     - `expiresAt` (unix timestamp)
   - **Rules:** provider must return at least one destination (`invoice` or `depositAddress`).

### Tier 2 — Strongly recommended for operator visibility
4. **Get fiat balance**
   - **Endpoint:** `POST /api/v1/user/get-balance/fiat`
   - **Request:** `{ "currency": "EUR" }`
   - **Response:** `{ "balance": "3816" }` (define unit explicitly: cents vs major unit)

5. **List transactions**
   - **Endpoint:** `POST /api/v1/account/transactions`
   - **Request:** `{ "startDate": 1700000000000, "endDate": 1700100000000 }`
   - **Response:**
     - `transactions[]` with: `orderId`, `type`, `subType`, `sourceAmount`, `sourceCurrency`, `destinationAmount`, `destinationCurrency`, `destinationAddress`, `status`, `createdAt`

### Tier 3 — Optional convenience
6. **Hosted onboarding callback URL creation**
   - **Endpoint:** `POST /api/v1/application/btcpay/signup-url`
   - **Request:** `{ "callback": "https://..." }`
   - **Response:** `{ "signupURL": "https://..." }`
   - **Purpose:** Better UX for API key provisioning; not required if operator enters API key manually.

## Cross-endpoint requirements (should be standardized)

1. **Authentication**
   - Header-based API key auth (`api-key` or `Authorization: Bearer ...`), documented once.

2. **Idempotency**
   - `POST /offramp/order` should support idempotency key (header or body field) to avoid duplicate orders during retries.

3. **Error schema**
   - Return structured errors with stable fields:
     - `message`
     - `statusCode`
     - `errorCode`
     - `errorMessage`
     - `errorDetails`

4. **Numeric encoding**
   - Decide and document if numeric fields are JSON numbers or strings.
   - For money, explicitly define units (sats, cents, BTC, EUR).

5. **Timeout and retry profile**
   - Rate and user-id endpoints: fast, retry-safe.
   - Order endpoint: idempotent retry only.

6. **Status model**
   - Transaction/order statuses must be finite and documented (e.g., `PENDING`, `SUCCESSFUL`, `FAILED`, `EXPIRED`).

## Plugin-clone implementation sequence (for another provider)

1. Fork/clone an existing withdrawal plugin structure as a new provider plugin.
2. Implement a provider client while preserving method parity for MVP (`GetUserId`, `GetRate`, `PlaceOrder`).
3. Keep existing BTCPay orchestration logic in service layer initially (threshold checks, payout claim creation, pending payout tracking).
4. Add provider-specific method mapping (`LIGHTNING`, `ON_CHAIN`) and minimum amount rules.
5. Wire API key validation to provider `user-id` endpoint.
6. Defer balance/transaction widget data if provider does not support Tier 2 at launch.
7. Add integration tests for:
   - invalid API key
   - quote retrieval
   - order creation returning invoice
   - order creation returning on-chain address
   - duplicate retry protection (idempotency)

## Recommended MVP cut
Ship with Tier 1 only + consistent error contract; add Tier 2 once provider can supply reliable balance/transaction data.

## Open decisions to confirm before implementation
1. Should provider API be versioned at `/api/v1/...` from day one?
2. Is idempotency mandatory for order creation in MVP, or acceptable in v2?
3. If both `invoice` and `depositAddress` are returned, which destination should plugin prioritize?
