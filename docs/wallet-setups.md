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
return 1 while preserving HTTP status. Other v2 commands include the problem
body; paper fill errors use a safe status-specific explanation and omit the
server body, storage details and credentials. Success returns 0. Amendments and
fill corrections require a single quoted strong ETag; wildcard and weak ETags
are rejected locally.

## Paper wallet fills

Paper fills are explicit, immutable wallet-scoped evidence records. Commands
operate on the selected user session and workspace. A fill stays unassigned, and
the response's `accounting` value remains API-owned; this CLI does not turn fills
into legacy executions, allocations, balances, PnL or performance. No command
places an order.

```sh
wickd wallet fills create wallet-id --request fill.json --idempotency-key fill-key
wickd wallet fills list wallet-id --limit 50
wickd wallet fills list wallet-id --cursor 'nextCursor-from-prior-page'
wickd wallet fills get wallet-id fill-id --cursor 'snapshotCursor-from-list'
wickd wallet fills correct wallet-id fill-id --request correction.json --if-match '"paper-fills-wallet-revision"' --idempotency-key correction-key
```

Creation accepts one exact API request, for example:

```json
{"instrumentId":"instrument-id","instrumentSpecVersionId":"spec-id","side":"buy","positionSide":"net","price":"100.25","quantity":"0.01","occurredAtUtc":"2026-10-14T08:00:00Z"}
```

Correction appends a replacement request and reason; it preserves the original
fill as history. Use the current wallet fills ETag from the latest read/write and
keep the request file and idempotency key unchanged when retrying. A changed
correction is a new logical write and needs a new key. List `nextCursor` pages
forward; `snapshotCursor` freezes later `get` reads. IDs and cursors are encoded
and sent to the API without local financial calculations.

## Paper wallet history and accounting receipts

History commands retain a user's explicit declarations about opening flatness
and whether manual fills are recorded for one wallet and instrument. The API
records the authenticated user and immutable revisions. `null` and `false` stay
distinct; missing history or an unknown opening is not inferred as flat.

```sh
wickd wallet history create wallet-id instrument-id --request history.json --if-match '"history-wallet-instrument-revision"' --idempotency-key history-key
wickd wallet history list wallet-id instrument-id --limit 50
wickd wallet history list wallet-id instrument-id --cursor 'nextCursor-from-prior-page'
wickd wallet history get wallet-id instrument-id attestation-id --cursor 'snapshotCursor-from-list'
wickd wallet history correct wallet-id instrument-id attestation-id --request correction.json --if-match '"history-wallet-instrument-revision"' --idempotency-key correction-key
```

Create and correction requests use the API's generated schemas. Keep the exact
request bytes, idempotency key and current strong history ETag for a retry. A
correction appends to the declared history; it does not erase the prior
attestation. The API decides whether a declaration qualifies as complete.

Accounting receipt commands ask the API to project retained native paper fills,
history declarations and instrument specifications for a wallet/instrument
scope. The only request field is the supported projection policy; the client
does not send lots, matches, fees, costs or financial totals:

```sh
wickd wallet accounting create wallet-id instrument-id --request accounting.json --idempotency-key accounting-key
wickd wallet accounting list wallet-id instrument-id --limit 50
wickd wallet accounting list wallet-id instrument-id --cursor 'nextCursor-from-prior-page'
wickd wallet accounting get wallet-id instrument-id receipt-id --cursor 'snapshotCursor-from-list'
```

The request is `{"policyVersion":"wallet-fifo-projection-v2"}`. Receipt
amounts remain decimal strings, and unknown fees, costs or net results remain
`null`. These receipts are retained FIFO projection evidence; they are not
allocations, balanced wallet postings, balances, equity, tax records or
performance claims. The CLI forwards their exact API payloads without
financial calculations.

## Paper activity allocations

Allocation commands append complete assignment views for one wallet and
instrument. Each assignment names a retained accounting receipt, prior revision,
reason and setup-attempt attribution rows in the request file. The API validates
those rows against its retained FIFO lots and matches. Use the list ETag for a
new write, keep request bytes and the idempotency key unchanged for a retry, and
pass a list's `snapshotCursor` to read a revision at that frozen publication:

```sh
wickd wallet allocations list wallet-id instrument-id --limit 50
wickd wallet allocations list wallet-id instrument-id --cursor 'nextCursor-from-prior-page'
wickd wallet allocations get wallet-id instrument-id revision-id --cursor 'snapshotCursor-from-list'
wickd wallet allocations assign wallet-id instrument-id --request assignments.json --if-match '"allocation-wallet-instrument-revision"' --idempotency-key assignment-key
```

Responses include API-derived unassigned entry-lot and exit-match quantities;
the client preserves them as decimal strings or `null` with a withholding reason.
It does not calculate remaining quantities, fills, PnL, costs, fees, net results,
balances or performance. An allocation records strategy attribution for
retained paper activity; it does not create an execution or wallet posting.

## Dispositions and note revisions

The lifecycle candidate adds explicit disposition and note commands. Each write
requires a request file, idempotency key and the current setup ETag:

```sh
wickd setup skip setup-id --request skipped.json --idempotency-key skip-key --if-match '"setup-1"'
wickd setup missed setup-id --request missed.json --idempotency-key missed-key --if-match '"setup-1"'
wickd setup cancel setup-id --request cancelled.json --idempotency-key cancel-key --if-match '"setup-1"'
wickd setup notes append setup-id --request note.json --idempotency-key note-key --if-match '"setup-1"'
wickd setup notes revise setup-id --request revised-note.json --idempotency-key note-revision-key --if-match '"setup-2"'
```

Use only the disposition that describes the opportunity. The examples above are
alternative commands, not a sequence of transitions to run on one setup. The API
decides which transitions are valid and retains disposition history. Refresh
current detail before a new logical write; use the unchanged original ETag,
request bytes and key when retrying an already submitted write.

Disposition files explicitly name the requested operation:

```json
{"command":"markSkipped","reason":"Entry conditions were not met.","effectiveAtUtc":"2026-10-14T08:05:00Z"}
```

`setup missed` requires `"command":"markMissed"`; `setup cancel` requires
`"command":"cancel"`. The CLI refuses a mismatched command and sends the
original file bytes unchanged.

Personal and observation notes retain prior content. An append supplies an
explicit null successor reference:

```json
{"category":"observation","body":"Wait for confirmation.","effectiveAtUtc":"2026-10-14T08:05:00Z","supersedesId":null}
```

A revision references the note being superseded, using a new logical key and
current setup ETag:

```json
{"category":"observation","body":"Confirmation arrived.","effectiveAtUtc":"2026-10-14T08:10:00Z","supersedesId":"previous-note-id"}
```

The API requires the latest note revision in the same category and validates
effective-time order. These commands append history; they do not edit or delete
prior note facts. Legacy response notes may omit `supersedesId`; the request
contract requires it explicitly as a string or null.

## Frozen setup detail

`setup list` now returns a required `body.snapshotCursor` as well as the optional
`body.nextCursor`. Keep them for their distinct purposes: `nextCursor` fetches the
next list page, while `snapshotCursor` reads setup detail at the list's retained
publication and knowledge cutoff, including when `nextCursor` is null.

```sh
wickd setup list --limit 50
wickd setup get setup-id --cursor 'snapshotCursor-from-list'
```

The CLI forwards the opaque token without reconstructing state locally. Omitting
`--cursor` reads current detail. Frozen responses retain their historical
publication watermark, `asOf` and ETag. Read current detail before starting a new
write; a historical ETag can be stale, which the API reports as HTTP 412. Tokens
remain scoped to the API-selected workspace; switching workspaces does not grant
access to a retained cursor. Existing 404/409/412/422/428 refusals remain visible.

## Native setup attempts

The native attempt candidate adds setup-scoped identity commands. Creation
requires an accepted plan revision, an admitted paper/backtest mode, the current
setup ETag and an explicit idempotency key:

```sh
wickd setup attempts create setup-id --request attempt.json --idempotency-key attempt-key --if-match '"setup-2"'
wickd setup attempts list setup-id --limit 50
wickd setup attempts get setup-id attempt-id
wickd setup attempts get setup-id attempt-id --cursor 'snapshotCursor-from-setup-or-attempt-list'
```

The create request contains only the plan identity, mode and decision time:

```json
{"acceptedPlanRevisionId":"plan-revision-id","mode":"paper","decisionAtUtc":"2026-10-14T08:10:00Z"}
```

The API verifies that the revision belongs to the setup. Attempt detail and list
responses are checked against the requested setup identity; the nested detail
command also checks the attempt ID. List `nextCursor` advances pagination, while
`snapshotCursor` freezes the publication and can be passed to `get`. The API
accepts either a setup-list or attempt-list snapshot cursor so both detail panels
can share one historical boundary. Creating an attempt records its identity and accepted preview; it does not create fills,
place orders or change setup status. Existing v1 executions remain separate.
