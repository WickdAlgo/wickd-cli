# Vendored Wickd API Contracts

These JSON Schemas are the public wire boundary consumed by `wickd-cli`. They
were verified against `wickd-api` commit `f7f9268` on 2026-08-28. The CLI has
no project or package reference to `wickd-api` or
`wickd-core`.

Update these files only when the corresponding API contract change is accepted.
Run `scripts/sync-contracts.sh /path/to/wickd-api`, inspect the diff, then run
the CLI tests and package-boundary gate.
