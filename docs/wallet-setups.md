# Wallet and setup client commands

These commands consume the API v2 candidate. Configure the host Bearer token
separately from the user session. Every command requires `--session-token` (or
`sessionToken` in config). Wallet, risk and setup commands also require a selected
workspace (`workspace use <ID>` or `--workspace-id`). Catalog reads require only
the authenticated session; they return shared asset and instrument identities.

```sh
wickd catalog assets
wickd catalog instruments
wickd catalog specs
wickd catalog timeframes
wickd wallet create --request wallet.json --idempotency-key opening-unique-key
wickd wallet list --limit 50
wickd wallet get wallet-id
wickd wallet balance wallet-id
wickd risk preview --request preview.json --idempotency-key preview-unique-key
wickd setup accept --request accept.json --idempotency-key acceptance-unique-key
wickd setup list --limit 50
wickd setup get setup-id
wickd setup amend setup-id --request amend.json --idempotency-key amendment-unique-key --if-match '"setup-1"'
```

Request files must be UTF-8 JSON, at most 1 MiB. The bundled API-generated
schemas validate required fields, enum tokens, dates and exact decimal strings.
The CLI sends the original bytes without rounding, financial calculations or
automatic retry. Keep the original request file, key, workspace and amendment
ETag for retries; a changed request needs a new logical key. Do not generate a
new key merely because a response was lost. The API decides whether a preview
is current, a setup can be accepted, or an amendment conflicts.

Every command prints JSON containing `status`, `eTag`, `location` and `body`.
The body retains the complete API envelope, exact decimal strings, server
knowledge times, publication watermark and cursor metadata. `--json` is accepted
for consistency; these commands always return JSON. List commands fetch exactly
one page. Supply its opaque `body.nextCursor` using `--cursor` with the same
workspace, for example `wickd wallet list --cursor 'opaque-cursor'`. A null cursor
marks the last page. Preserve publication metadata when interpreting pages.

A wallet opening request is explicit about its population and opening capital:

```json
{"name":"Research paper","kind":"paper","baseAssetId":"asset-id","openingQuantity":"10000","openingAtUtc":"2026-10-14T08:00:00Z"}
```

A risk preview contains the complete plan, including every target:

```json
{"plan":{"walletId":"wallet-id","instrumentId":"instrument-id","direction":"long","entryPrice":"50000","stopPrice":"49000","sizingKind":"riskAmount","sizingValue":"100","targets":[{"ordinal":1,"price":"52000","exitFraction":"1"}]},"instrumentSpecVersionId":"spec-id","decisionAtUtc":"2026-10-14T08:00:00Z","balanceBasisReceiptId":"basis-id"}
```

Use the preview ID returned by the API when accepting:

```json
{"previewId":"preview-id","strategyVersionId":"published-version-id","primaryTimeframeId":"timeframe-id","source":"self","correlationId":null,"rationale":"Observed continuation.","invalidation":"Thesis fails below the stop."}
```

For an amendment, obtain a fresh preview of the complete replacement plan, then
submit its preview ID and the setup's current ETag:

```json
{"previewId":"new-preview-id","effectiveAtUtc":"2026-10-14T08:05:00Z","reason":"New observation changed the complete plan."}
```

Identifiers above are placeholders, never seeded fallback records. Read actual
catalog, wallet and strategy IDs from the configured API. Decimal values must be
strings; nullable properties required by the schema must be present as `null`.
The server owns sizing, original risk and retained provenance. `wallet balance`
returns a retained opening basis, not a real-time portfolio balance. The current
API qualifies paper/backtest, linear same-asset sizing and rejects live wallet
creation until a verified connection scope exists. No CLI command places orders,
acquires provider history or fabricates balances/FX.

Local request/schema/path/header validation returns exit code 2. Remote/API errors
return 1 with the server's exact HTTP status and problem body (including 404,
409, 412, 422 and 428); success returns 0. Amendments require a single quoted
strong ETag; wildcard and weak ETags are rejected locally.
