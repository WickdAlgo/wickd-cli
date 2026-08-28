#!/bin/sh
set -eu

api_root=${1:?usage: scripts/sync-contracts.sh /path/to/wickd-api}
cli_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)

for name in \
  account-risk.v1.schema.json \
  accounts.v1.schema.json \
  backtest-request.v1.schema.json \
  backtest.v1.schema.json \
  dataset-alias-request.v1.schema.json \
  dataset-alias.v1.schema.json \
  fetch.v1.schema.json \
  inspection-dataset.v1.schema.json \
  run-listing.v1.schema.json \
  supported-instruments.v1.schema.json \
  trade-detail.v1.schema.json \
  trade-summary.v1.schema.json \
  vwap-analysis-request.v1.schema.json \
  vwap-analysis.v1.schema.json
do
  cp "$api_root/contracts/$name" "$cli_root/contracts/$name"
done

for name in fetch.v1.sample.json backtest.v1.sample.json vwap-analysis.v1.sample.json
do
  cp "$api_root/contracts/samples/$name" "$cli_root/contracts/samples/$name"
done
