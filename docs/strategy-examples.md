# Owner-private strategy illustration commands

`strategy example create <STRATEGY_ID> --request request.json --idempotency-key KEY`
retains the exact JSON request bytes. The canonical request names `versionId`,
`snapshotId`, `observationId`, `category` and human `annotation`. Use the same file
and key to retry an uncertain result. It references existing retained evidence;
creating a reference does not acquire or upload data.

`strategy example list <STRATEGY_ID> [--limit 1..200] [--cursor CURSOR]` and
`strategy example get <STRATEGY_ID> <EXAMPLE_ID> [--cursor CURSOR]` default to
`decisionOnly`. Add `--category outcomeExample` to explicitly read retrospective
references. References always declare `humanIllustrative`: they do not prove the
engine produced the occurrence under the human strategy version, and prose is
not certified free of hindsight.

`strategy example evidence <STRATEGY_ID> <EXAMPLE_ID> [--cursor CURSOR]` reads the
exact decision-only reference first, then requests its existing server-truncated
research prefix with the same frozen cursor and verifies the returned snapshot
and observation identities. Outcome references are refused for this command.
Financial and market computations remain on the API.

Commands require a session and selected workspace, preserve server JSON and HTTP
status/ETag/location in the output envelope, and retain opaque cursors exactly.
Only reuse a cursor for the same actor, strategy and category. Request files are
bounded to 64 KiB; local validation exits 2, API/runtime failures 1, success 0.

Schemas and samples are copied byte-for-byte from API105 candidate `c63c1ed`:
three v2 illustration contracts and the existing v1 research-example contract.
No engine or adapter assemblies are added. These source commands do not imply
published packages, deployed routes or real-data admission.
