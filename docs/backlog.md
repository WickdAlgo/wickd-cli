# Client backlog

## CLI-DOC-001 — boundary/documentation reconciliation

Status: documentation prepared, 2026-09-08; not release-relevant.
Correct repository links, remote/local boundaries and command terminology.
Validation: source command registration, package identity, Markdown links and
git diff --check. No command implementation or service-availability claim.

## CLI-BL-001 — API compatibility and evidence display

Status: Candidate; no implementation claimed.
After versioned server research/setup contracts are served, decide the minimal
CLI adoption needed by research users. Preserve dataset/run-qualified references
and server error semantics; do not add detector code or a duplicate inspector.
Validation: HTTP contract fixtures plus authenticated server smoke test, package
boundary check and CLI help/exit-code tests. Dependency: served API capability.

