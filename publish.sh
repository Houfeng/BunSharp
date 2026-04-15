#!/usr/bin/env bash
set -euo pipefail

if [[ -z "${NUGET_KEY:-}" ]]; then
  echo "Error: NUGET_KEY environment variable is not set." >&2
  exit 1
fi

echo "==> Cleaning previous builds..."
rm -rf artifacts/nuget

echo "==> Building BunSharp.Generator..."
dotnet build BunSharp.Generator/BunSharp.Generator.csproj -c Release

echo "==> Packing BunSharp..."
dotnet pack BunSharp/BunSharp.csproj -c Release -o artifacts/nuget

echo "==> Pushing to NuGet..."
dotnet nuget push "artifacts/nuget/BunSharp.*.nupkg" \
  --api-key "$NUGET_KEY" \
  --source https://api.nuget.org/v3/index.json \
  --skip-duplicate

echo "==> Done."
