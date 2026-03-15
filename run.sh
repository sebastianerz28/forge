#!/bin/bash
cd "$(dirname "$0")"
export GITHUB_TOKEN=$(gh auth token)
export FORGE_DB_URL="postgresql://factory:factory_local@localhost:5432/factory_db"
unset CLAUDECODE
dotnet run --project forge/src/Forge.Runner "$@"
