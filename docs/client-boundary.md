# Public client boundary

The public wickd tool sends requests to the WickdAlgo API and renders or saves its
responses. It accepts CLI options, local configuration and credentials; it returns
console/JSON/JSONL output and documented exit codes.

It never runs proprietary market detectors, resolves market-model ambiguity,
sizes real positions independently or bundles engine/inspection/exchange assemblies.
It is not the internal developer CLI used to debug the engine locally.
Research and educational users can inspect the API's outputs; endpoint availability
is determined by the configured server, not by the client's command name.

Local configuration and explicit output files are a local footprint; remote
computation does not mean the client writes nothing locally.
run means structure replay. A trade simulation is a different operation; do not
describe run output as a strategy's economic result.

Public product information lives in the [organization profile](https://github.com/WickdAlgo).
Private implementation/definition documents are not duplicated or linked here.
[Client backlog](sprints/backlog.md) tracks only client-specific work.

Package boundary verification uses scripts/verify-package.sh with an existing
nupkg path. A successful package check is not proof of a live authenticated server.
The repository's CI builds/tests/packs and smoke-tests the tool. Publication and
service availability are separate release facts.
