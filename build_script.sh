#!/usr/bin/env bash
set -euo pipefail
DOTNET_ROOT="${HOME}/.dotnet"
export DOTNET_ROOT
export PATH="$DOTNET_ROOT:$PATH"
echo "dotnet=$(dotnet --version)"
echo "pwd=$(pwd)"
echo "ls=$(ls -la)"
dotnet --list-sdks
