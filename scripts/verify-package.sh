#!/bin/sh
set -eu

package=${1:?usage: scripts/verify-package.sh /path/to/wickd-cli.nupkg}

if unzip -Z1 "$package" | grep -E '(^|/)(Wickd\.(Core|Inspection|Adapters)(\.|/)|[^/]*Ccxt[^/]*\.dll$)'
then
  echo "error: proprietary engine or exchange-adapter content found in $package" >&2
  exit 1
fi

echo "clean package boundary: $package"
