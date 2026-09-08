# Product Backlog

Canonical pool for planned `wickd-cli` work.

## Done

### WKD-BL-030: The CLI Becomes A Thin Remote Client

- **State:** Done — 2026-08-28. Implementation pull request #1 landed on
  `dev` as `9e42d63`; promotion pull request #2 landed on `stage` as
  `4a06051`. Publication and `stage -> main` remain outside this item.
- **User value:** keep `wickd` publicly installable while proprietary engine
  computation runs only on `wickd-api`.
- **Acceptance:** command parsing, authentication, configuration, validation,
  request/response handling, and output formatting only; no Core, Inspection,
  adapter, CCXT, or local computation assembly direct or transitive.
- **Validation:** the existing command shapes complete fetch, saved-dataset
  analyze, and saved-dataset backtest against a running API; API-published
  contracts gate the wire models; the fresh package contains no proprietary
  assembly; 27 tests and all package/output boundaries are perturbed.
- **Related:** `WA-CLI-03`; API repair slice `API-BL-003` / `WA-CLI-02`.


## 2026-09-08 documentation and compatibility follow-up

Consolidated from the extra docs/backlog.md introduced during documentation
cleanup. This file remains the one local backlog; historical IDs are unchanged.

### CLI-DOC-001 — boundary/documentation reconciliation

Status: documentation prepared, 2026-09-08; not release-relevant.
Correct repository links, remote/local boundaries and command terminology.
Validation: source command registration, package identity, Markdown links and
git diff --check. No command implementation or service-availability claim.

### CLI-BL-001 — API compatibility and evidence display

Status: Candidate; no implementation claimed.
After versioned server research/setup contracts are served, decide the minimal
CLI adoption needed by research users. Preserve dataset/run-qualified references
and server error semantics; do not add detector code or a duplicate inspector.
Validation: HTTP contract fixtures plus authenticated server smoke test, package
boundary check and CLI help/exit-code tests. Dependency: served API capability.
