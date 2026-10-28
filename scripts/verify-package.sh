#!/bin/sh
set -eu

package=${1:?usage: scripts/verify-package.sh /path/to/wickd-cli.nupkg}

if ! entries=$(unzip -Z1 "$package"); then
  echo "error: unable to inspect package $package (unzip required)" >&2
  exit 1
fi

if printf '%s\n' "$entries" | grep -iE '(^|/)(Wickd\.(Core|Inspection|Adapters)(\.|/)|[^/]*ccxt[^/]*\.dll$)'
then
  echo "error: proprietary engine or exchange-adapter content found in $package" >&2
  exit 1
fi

echo "clean package boundary: $package"
