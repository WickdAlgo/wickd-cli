# Changelog

All notable changes to the public WickdAlgo CLI are recorded here.

Current work is tracked in GitHub Issues; the
[WickdAlgo Project](https://github.com/orgs/WickdAlgo/projects/1) is optional. The former
backlog and sprint records are retained in [`docs/sprints/`](docs/sprints/);
[`docs/issue-migration.json`](docs/issue-migration.json) maps legacy IDs.

## Unreleased

### Added
- Add authenticated `performance report create|get|revise` commands for retained
  native-paper setup reports, preserving request bytes, idempotency keys, strong
  ETags and server-reported metrics. Refs #39; requires API #133.

- Add authenticated owner-private `strategy example create|list|get|evidence`
  commands with canonical API validation, exact request retries and frozen
  cursors. Decision-only references are the default; outcome references require
  explicit selection, and evidence verifies the exact retained source. CLI #37.

- Add native setup review append/latest/revision/history commands using the
  API-generated immutable review contracts. Preserve explicit version-specific
  rule assessments, no-review state, exact request retries, review ETags and
  review-stream cursors without client inference or financial calculations.
  Refs #35.

- Add session-definition and market-condition catalogs, draft strategy
  instrument/timeframe and context scopes, and append-only setup context
  observations. Add exact session-version and stable condition-tag filters to
  setup list/detail/context reads. Validate API-generated contracts, forward
  exact request bytes, and preserve strong ETags and frozen cursors. Refs #33.

- Add root-scoped paper cost attribution list/get/assign commands, exact request
  retries, strong resource ETags and frozen reads. Preserve server amounts and
  reconciliation evidence without financial calculations. Validate exact .NET
  decimal coefficients for manual costs and attribution amounts without rounding.
  Refs #31.

- Add wallet-scoped paper cost create/list/get/correct commands with exact
  generated contract validation, idempotent request forwarding, correction
  ETags and frozen reads. Preserve signed decimal strings and nullable unknown
  amounts; cost coverage remains API-owned and unknown. Refs #29.

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
