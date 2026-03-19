#!/bin/bash
set -e

RIDS=(osx-x64 osx-arm64 win-x64 win-arm64 linux-x64 linux-arm64)

for rid in "${RIDS[@]}"; do
  echo "Publishing for $rid..."
  dotnet publish src/ZapiCli -o "build/$rid" -c Release -r "$rid" -p:DebugType=none
  echo "Done: build/$rid"
  echo ""
done

echo "All platforms published."
