# Wickd CLI (`wickd`)

Repository scope, current-versus-target behavior, and documentation routing: [client boundary](docs/client-boundary.md).

[![CI](https://github.com/WickdAlgo/wickd-cli/actions/workflows/ci.yml/badge.svg)](https://github.com/WickdAlgo/wickd-cli/actions/workflows/ci.yml)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE)

Official command-line client for the **WickdAlgo** algorithmic trading, market structure journaling, and deterministic backtesting platform.

---

## 🏛 Architecture & Topology

Per **ADR 002**, `wickd-cli` is a **thin, high-performance remote client**. Proprietary core computation runs securely server-side on `wickd-api`, and `wickd-cli` never bundles or distributes proprietary engine binaries (`Wickd.Core`).

```text
wickd-cli (Public CLI)  ──HTTPS / Bearer Auth──►  wickd-api (Platform Backend)  ──►  wickd-core (Engine)
```

- **Physical Separation:** The CLI communicates with `wickd-api` via HTTP/REST contracts.
- **Fast & Responsive:** Built on modern .NET 10 with rich console rendering via Spectre.Console.
- **Remote computation:** Replays, session VWAP analytics, and trade journal reads are requested over HTTPS. Local configuration, credentials and requested output files still have a local footprint.

---

## 🚀 Installation

### As a .NET Global Tool

```bash
dotnet tool install -g wickd-cli
```

To update to the latest version:

```bash
dotnet tool update -g wickd-cli
```

### Build From Source

```bash
git clone https://github.com/WickdAlgo/wickd-cli.git
cd wickd-cli
dotnet build
dotnet run --project src/Wickd.Cli -- --help
```

---

## ⚡ Quick Start

```bash
# 1. Initialize user configuration (~/.wickd/config.json)
wickd config init

# 2. Authenticate with your platform API token (or run anonymous in dev)
wickd auth login

# 3. Check connectivity with Wickd API
wickd auth status

# 4. Fetch and cache market data with an alias
wickd fetch --market BTC_USDT_PERP --timeframe 4h \
  --from 2026-07-01T00:00:00Z --to 2026-08-02T08:00:00Z \
  --alias jul-btc

# 5. Run VWAP and volume anomaly analysis
wickd analyze vwap --dataset jul-btc --periods daily,weekly --out runs/vwap.jsonl

# 6. Run the structure engine over a cached dataset
wickd run --dataset jul-btc --run-id jul-btc-smoke

# 7. View structure runs and trade journal
wickd manage runs list
wickd trades list
wickd accounts list
```

---

## 📖 Command Reference

### `wickd fetch`
Downloads candles from the exchange and caches them on the platform.

```bash
wickd fetch --market <MARKET> --timeframe <TIMEFRAME> --from <UTC> --to <UTC> [--exchange <ID>] [--alias <NAME>] [--force]
```

| Option | Description |
|---|---|
| `-m`, `--market <MARKET>` | Canonical market ID (e.g. `BTC_USDT_PERP`). Defaults to config. |
| `-t`, `--timeframe <TF>` | Candle timeframe (e.g. `5m`, `1h`, `4h`, `1d`). Defaults to config. |
| `--from <UTC>` | Inclusive UTC start timestamp in ISO-8601 format (`2026-07-01T00:00:00Z`). |
| `--to <UTC>` | Exclusive UTC end timestamp in ISO-8601 format (`2026-08-02T08:00:00Z`). |
| `-e`, `--exchange <EXCHANGE>` | Target exchange ID (e.g. `binance`, `bybit`). |
| `-a`, `--alias <NAME>` | Save range under a friendly alias name for later `--dataset` use. |
| `-f`, `--force` | Overwrite an existing alias of the same name. |

---

### `wickd run`
Runs the structure engine over a cached dataset. This is a structure-determination
run, not a strategy backtest.

```bash
wickd run (--dataset <ALIAS> | --market <M> --timeframe <TF> --from <F> --to <T>) [--run-id <ID>]
```

| Option | Description |
|---|---|
| `-d`, `--dataset <ALIAS>` | Saved dataset alias name. |
| `--market`, `--timeframe`, `--from`, `--to` | Explicit market and date range (alternative to `--dataset`). |
| `-r`, `--run-id <ID>` | Custom run ID (generated automatically if omitted). |

---

### `wickd analyze vwap`
Computes session VWAPs, previous-period closing VWAP liquidity levels, and volume classifications.

```bash
wickd analyze vwap (--dataset <ALIAS> | --market <M> --timeframe <TF> --from <F> --to <T>) [--periods <LIST>] [--level-periods <LIST|none>] [--out <PATH>]
```

| Option | Description |
|---|---|
| `-d`, `--dataset <ALIAS>` | Saved dataset alias name. |
| `--periods <LIST>` | Comma-separated running-series periods (`daily,weekly,monthly,quarterly,yearly`). |
| `--level-periods <LIST\|none>` | Previous-close level periods, or `none` to disable levels. |
| `-o`, `--out <PATH>` | Export full result records to a JSONL file. |

---

### `wickd manage`
Dataset and backtest run management.

```bash
# Datasets / Aliases
wickd manage datasets list
wickd manage datasets delete --alias <NAME>

# Runs
wickd manage runs list
wickd manage runs get --run-id <ID>
```

---

### `wickd trades` & `wickd accounts`
Trade journal and account risk inspection.

```bash
# Trade journal
wickd trades list
wickd trades get --trade-id <ID>

# Accounts & risk
wickd accounts list
wickd accounts risk --account-id <ID>
```

---

### `wickd config` & `wickd auth`
Manage local settings and credentials.

```bash
# Configuration
wickd config init [--force]
wickd config path
wickd config get [KEY]
wickd config set <KEY> <VALUE>

# Authentication
wickd auth login [--token <TOKEN>]
wickd auth status
```

---

## ⚙️ Global Options

Every command supports:

- `-c`, `--config <PATH>`: Load custom configuration file.
- `--api-url <URL>`: Override platform API URL.
- `--token <TOKEN>`: Override API Bearer token.
- `--json`: Output raw structured JSON.
- `-h`, `--help`: Display context-sensitive help.

---

## 🛠 Configuration Resolution Order

Configuration is loaded in the following order:
1. `--config <path>` command option
2. `WICKD_CONFIG` environment variable
3. Current directory `./wickd.json`
4. User profile `~/.wickd/config.json`
5. Default fallback configuration

Example `config.json`:

```json
{
  "apiUrl": "http://localhost:5081",
  "apiToken": null,
  "defaultExchange": "binance",
  "defaultMarket": "BTC_USDT_PERP",
  "defaultTimeframe": "4h",
  "timeoutSeconds": 60,
  "structure": {
    "pivotStrength": 2
  },
  "vwap": {
    "enabledPeriods": [ "daily", "weekly" ],
    "previousLevelPeriods": [ "daily", "weekly" ],
    "volumeLength": 20,
    "mediumThreshold": 1.5,
    "largeThreshold": 2.5,
    "lowVolumeThreshold": -1.0,
    "showLowVolume": true
  }
}
```

---

## 🧪 Testing

Run the test suite with:

```bash
dotnet test
```

---

## 📜 License

Distributed under the **Apache License 2.0**. See [`LICENSE`](LICENSE) for details.
