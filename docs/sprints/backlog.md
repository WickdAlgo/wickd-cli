# Product Backlog

Canonical pool for planned `wickd-cli` work.

## In Progress

### WKD-BL-030: The CLI Becomes A Thin Remote Client

- **State:** In Progress — reconciled 2026-08-27 against the initial repository
  commit and the live `wickd-api` contracts.
- **User value:** keep `wickd` publicly installable while proprietary engine
  computation runs only on `wickd-api`.
- **Acceptance:** command parsing, authentication, configuration, validation,
  request/response handling, and output formatting only; no Core, Inspection,
  adapter, CCXT, or local computation assembly direct or transitive.
- **Validation:** the existing command shapes complete fetch, saved-dataset
  analyze, and saved-dataset backtest against a running API; API-published
  contracts gate the wire models; the fresh package contains no proprietary
  assembly; the package boundary is perturbed.
- **Related:** `WA-CLI-03`; API repair slice `API-BL-003` / `WA-CLI-02`.
