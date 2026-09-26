# Repository Guidelines: wickd-cli

## GitHub work approval

Owning Issues and the shared WickdAlgo Project hold current work state. A brief
idea may be title only. Inbox/Candidate, an imported source-era Ready status,
and a migration/history receipt do not authorize new implementation. Confirm
actual maintainer-approved scope and acceptance before taking Ready work;
checkpoint evidence, blockers and next action on the owning issue. Preserve
existing review, release, privacy, account and acquisition boundaries.


For repository ownership and current-versus-target contracts, read [docs/client-boundary.md](docs/client-boundary.md). Keep public documentation free of private implementation and research definitions. Existing workflow and data-access restrictions below remain in force.

`wickd-cli` is the public command-line interface for the WickdAlgo algorithmic trading and market analysis platform.

## Architecture & Boundary Rules (ADR 002)

```text
public wickd CLI  --HTTPS/auth-->  wickd-api  --private NuGet-->  wickd-core
```

- **The CLI is a thin remote client.** All backtest computation, structure detection, and VWAP computation run server-side via `wickd-api`.
- **`wickd-cli` MUST NEVER bundle or reference `Wickd.Core`, `Wickd.Inspection`, or proprietary engine assemblies.**
- **Contracts and DTOs are client-side models** serializing over standard JSON/HTTPS to `wickd-api`.
- **Configuration** is managed locally in `~/.wickd/config.json` or overridden by `--config` / `WICKD_CONFIG`.

## Work Intake And Delivery

Track new feature ideas, bug reports, maintenance, and documentation work in
GitHub Issues and the [WickdAlgo Project](https://github.com/orgs/WickdAlgo/projects/1).
The former `docs/sprints/backlog.md` is a historical snapshot. Preserve its
legacy keys and use [`docs/issue-migration.json`](docs/issue-migration.json) to
find their current Issues. Do not add backlog rows or require weekly sprint
bookkeeping commits. Keep acceptance, validation and current implementation
status in the owning Issue; accepted work is tracked in the Project. Existing
sprint records and changelog remain historical delivery and release evidence.
Existing package, privacy, and release-approval boundaries remain unchanged.

## Build & Test Commands

```bash
dotnet restore Wickd.Cli.slnx
dotnet build Wickd.Cli.slnx
dotnet test Wickd.Cli.slnx
dotnet run --project src/Wickd.Cli -- --help
```

## Packaging & Verification

- The tool is packed with `<PackAsTool>true</PackAsTool>` and executable name `wickd`.
- Release verification gate ensures no proprietary engine assembly is ever bundled in `wickd.*.nupkg`:
  ```bash
  scripts/verify-package.sh artifacts/wickd-cli.*.nupkg
  ```
  This command must return exit code 0; it refuses Core, Inspection, adapter,
  and CCXT package entries.

## Coding Style & Standards

- Target Framework: .NET 10.0 (`net10.0`) / C# 13.
- CLI Framework: `Spectre.Console.Cli` + `Spectre.Console`.
- DI: `Microsoft.Extensions.DependencyInjection` via `TypeRegistrar`.
- Exit codes: `0` (Success), `1` (Runtime/API error), `2` (Validation/Argument error).
- Four-space indentation, nullable enabled, explicit typing.
