# Retained market evidence reads

`wickd history snapshot get <ID>` requests one exact immutable snapshot from the
configured API. The response includes source/instrument/timeframe identity,
normalization version, selected revision/object hashes, explicit coverage and
bootstrap limitations, and decimal-string candles. The CLI validates the generated
contract and prints the API result as JSON without financial calculations.

`wickd history series coverage <ID>` requests the API's checked coverage facts.
An empty list means no coverage observations were returned; it does not mean
complete history. Unavailable intervals and source limitations remain visible.

Both commands require a current user session and selected workspace, using the
existing `sessionToken`/`workspaceId` configuration or `--session-token` and
`--workspace-id` overrides. The optional service bearer is a separate host
boundary. Session values and local configuration paths are not printed by these
commands. Source rights are rechecked by the API; retaining a snapshot does not
grant access after entitlement revocation.

A 404 reports that the requested evidence is unavailable in the selected
workspace. A 503 reports unavailable/unverified retained storage. Both exit with
code 1 and retain the HTTP status. Arbitrary server error bodies and reason
phrases are omitted so storage paths or credentials cannot leak through an error.
Malformed or unsupported successful responses also fail explicitly, with no sample
or cache fallback. Invalid identity arguments exit with code 2 before network I/O.

The vendored schemas/samples are exact public artifacts from API candidate
`4598d904c03871edd6d5de58ee2904ee1db1961d` (API PR #77). No engine package,
provider download, acquisition permission, full-bootstrap qualification or local
financial calculation is included. Native weekly/monthly evidence does not imply
that the server's engine supports analysis at those timeframes.
