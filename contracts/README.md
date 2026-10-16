# Vendored Wickd API Contracts

These JSON Schemas are the public wire boundary consumed by `wickd-cli`.
The v1 set is retained from the previously verified API baseline; the v2
workspace and strategy schemas and the canonical `/api/runs` request/result
schemas are synchronized from `wickd-api/contracts`. The CLI has no project or
package reference to `wickd-api` or `wickd-core`.

Update these files only when the corresponding API contract change is accepted.
Run `scripts/sync-contracts.sh /path/to/wickd-api`, inspect the diff, then run
the CLI tests and package-boundary gate. v2 workspace calls use
`X-Wickd-Session` and `X-Wickd-Workspace`; the service Bearer token remains a
separate host boundary. No financial calculations are performed by this CLI.

Catalog, wallet and accepted-setup v2 schemas and synthetic samples are pinned
to API candidate `dd9d43c`. The CLI embeds these schemas and validates JSON
without converting financial strings to machine numbers. The sync script copies
the same public generated artifacts; review the producer SHA and diff on update.

Setup dispositions, note requests, setup detail and list artifacts are synchronized
from lifecycle checkpoint `3b9fa78`. List `snapshotCursor` is required; response
note `supersedesId` is optional to retain compatibility with earlier v2 receipts.
The CLI forwards frozen detail cursors and never reconstructs setup history.

Shared-history snapshot and coverage schemas/samples are synchronized from API
candidate `4598d904c03871edd6d5de58ee2904ee1db1961d`. The CLI validates exact
public wire artifacts and preserves decimal strings; source entitlement remains
an API decision. See `docs/market-history.md` for command and refusal behavior.

Native setup attempt request, detail and list artifacts are synchronized from
API checkpoint `e5affd7`; API follow-up `3eb0208` adds shared setup-list snapshot
cursor compatibility. They retain setup-scoped attempt identity and admitted
paper/backtest mode; they do not expose legacy execution or fill routes.

Native paper fill create/correction requests, detail/list responses and
synthetic samples are synchronized byte-for-byte from API checkpoint
`08d8f8164635635e0cced48ea3b2d208aa586300`. They retain decimal strings and
unprojected accounting metadata. The public CLI only captures and reads immutable
evidence; it does not calculate or allocate wallet economics or place orders.

Paper wallet cost create/correction requests, detail/list responses and
synthetic samples are pinned byte-for-byte from API checkpoint `75702ef`.
Nullable signed amounts remain exact strings when present; a null amount stays
unknown. The API owns cost coverage and all ledger effects.
