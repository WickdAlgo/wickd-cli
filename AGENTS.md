# Repository Guidelines: wickd-cli

## GitHub Work Flow

GitHub Issues are the durable record for current work; linked pull requests hold
implementation and validation evidence. The WickdAlgo Project is an optional
view and does not authorize work. Search related Issues before creating one.
Ideas and bugs can start as a title with free-form detail. Capturing or updating
an Issue records the idea only; it does not authorize implementation.

When asked to plan and implement, discover relevant open and historical Issues,
inventory the requested scope, and put bounded acceptance criteria and checks
on the owning Issues. Keep any detailed implementation plan temporary: use it
while working, never commit it, and delete it when the work completes or is
cancelled. Implement and run relevant checks, then open pull requests linked to
their Issues with `Refs #N` (or equivalent non-closing links). Do not use GitHub
auto-close keywords. Use repository labels when they help review or routing; no
board status, priority, rank, lease, or structured checkpoint is required.

After merge, reconcile each linked Issue with the merged change. Manually close
it only when its full scope and required human or deployment acceptance are
complete; otherwise leave it open with the remaining work. At the start of a
resumed task, check whether a related PR merged while its Issue stayed open. Do
not keep a separate work ledger or session log. Preserve existing package, privacy, and release-approval
boundaries.

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

Use the GitHub work flow above for issue discovery, implementation, and merge
reconciliation. The former `docs/sprints/backlog.md` and delivery records are
historical; preserve legacy keys and use [`docs/issue-migration.json`](docs/issue-migration.json)
when tracing them. Do not add backlog rows or require weekly sprint commits.
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
