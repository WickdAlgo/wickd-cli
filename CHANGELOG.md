# Changelog

All notable changes to the public WickdAlgo CLI are recorded here.

Current work is tracked in GitHub Issues; the
[WickdAlgo Project](https://github.com/orgs/WickdAlgo/projects/1) is optional. The former
backlog and sprint records are retained in [`docs/sprints/`](docs/sprints/);
[`docs/issue-migration.json`](docs/issue-migration.json) maps legacy IDs.

## Unreleased

### Added

- Add native paper wallet fill create/list/get/correct commands, validated against
  the API's generated contracts. Preserve exact request bytes, require explicit
  keys and a strong correction ETag, and retain workspace snapshot cursors. Fill
  evidence remains unassigned and `unprojectedIncomplete`; the CLI does not
  calculate accounting, allocate fills, or place orders. Refs #23.

- Add authenticated `history snapshot get` and `history series coverage` reads,
  exact generated contract validation and safe explicit 404/503 errors. No local
  market acquisition or financial calculations. Refs #19.

- Add setup skip/missed/cancel and append/revise note commands with exact JSON
  requests, explicit keys and ETags. Support frozen detail cursors and required
  setup list snapshot metadata while retaining legacy note compatibility.

- Add native setup attempt create/list/get commands for one setup. Validate the
  canonical paper/backtest request and response contracts, require the current
  setup ETag and stable idempotency key for creation, and support frozen reads.
  These commands record attempt identity only; fills and wallet economics remain
  server-owned follow-on work. Refs #21.

- Add authenticated catalog, wallet, risk preview and accepted setup commands
  with generated-schema JSON validation, explicit idempotency keys, amendment
  ETags, exact decimal transport and retained cursor/HTTP metadata.

- Add separate user session/workspace configuration, membership-verified workspace
  selection and paginated StrategyBook reads. Keep host authentication separate.
- Align structure runs with the canonical `/api/runs` contract and resolve
  friendly dataset aliases before submitting their complete cached identity.

### Removed

- Drop the no-op `-v` / `--verbose` global option and its README listing.
- Remove unused `RenderBanner` / `RenderInfo` renderer surface.
- Remove unused `Microsoft.Extensions.Configuration*` package references.
- Change dataset-alias delete to `Task` and drop the unreachable "not found" branch.

### Documentation

- Record the maintainer-provided attribution for the initial thin-client
  implementation merged through cli#1–#4: generated with Antigravity
  (Gemini). Those commits predate the repository's attribution-footer
  convention and remain authored by Burak; this entry records provenance
  without rewriting Git history.
