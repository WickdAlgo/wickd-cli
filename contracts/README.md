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
