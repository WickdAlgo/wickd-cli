# Human strategy context

These authenticated workspace commands retain explicit human definitions and
observations. The API owns persistence, publication order and authorization; the
CLI validates generated v2 contracts, forwards exact request bytes, and returns
the server envelope. It does not classify markets, infer sessions, or calculate
financial results.

Every command requires a user session token and selected workspace. Write
commands also require a request file and a unique idempotency key. Keep the
original bytes and key for retries. Version, revision, strategy-scope and setup
context writes require a current strong ETag; refresh the matching read before a
new logical write. Strategy scope resources share the strategy draft ETag, while
setup context observations use an independent context ETag.

```sh
wickd context sessions create --request session.json --idempotency-key session-key
wickd context sessions revise session-id --request session-version.json --if-match '"session-1"' --idempotency-key session-revision-key
wickd context sessions list --limit 100
wickd context sessions list --cursor 'nextCursor-from-prior-page'
wickd context sessions get session-version-id

wickd context conditions create --request condition.json --idempotency-key condition-key
wickd context conditions revise condition-tag-id --request condition-revision.json --if-match '"condition-1"' --idempotency-key condition-revision-key
wickd context conditions list --limit 100
wickd context conditions get condition-revision-id

wickd strategy scope get strategy-version-id
wickd strategy scope update strategy-version-id --request scope.json --if-match '"draft-1"' --idempotency-key scope-key
wickd strategy scope history strategy-id --limit 100
wickd strategy context get strategy-version-id
wickd strategy context update strategy-version-id --request context-scope.json --if-match '"draft-1"' --idempotency-key context-scope-key

wickd setup context get setup-id --session-version session-version-id --condition-tag condition-tag-id
wickd setup context append setup-id --request observation.json --if-match '"setup-context-1"' --idempotency-key observation-key
wickd setup context history setup-id --limit 100
```

The session and condition catalogs distinguish stable identities from immutable
versions/revisions. Strategy scope history returns the cursor for a frozen
publication; pass its `nextCursor` to the corresponding scope `get` command to
read that frozen version. Updates apply to drafts. Setup context reads accept
either or both filters and can use a frozen cursor. Setup context history returns append-only
observations. Session-version and condition-revision writes return the ETag
needed for the next version/revision write; their catalog reads do not expose a
concurrency ETag. Session/condition IDs and cursors are opaque API values; pass
them back unchanged. Context request files are limited to 64 KiB and validated
against the vendored API-generated schemas.

Setup list and detail can also filter by `--session-version` and
`--condition-tag`. These filters match exact session-definition versions and
stable market-condition tag identities. They do not classify a setup or alter
its retained context.
