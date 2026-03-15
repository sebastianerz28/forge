#!/bin/bash
set -e

# Install .NET 10 SDK
if ! command -v dotnet &> /dev/null || ! dotnet --list-sdks | grep -q "^10\."; then
    echo "Installing .NET 10 SDK..."
    curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir "$HOME/.dotnet"
    export DOTNET_ROOT="$HOME/.dotnet"
    export PATH="$DOTNET_ROOT:$PATH"
    echo ""
    echo "Add to your shell profile:"
    echo '  export DOTNET_ROOT="$HOME/.dotnet"'
    echo '  export PATH="$DOTNET_ROOT:$PATH"'
    echo ""
fi

# Build Forge
cd "$(dirname "$0")/forge"
echo "Restoring and building Forge..."
dotnet build
echo ""
echo "Forge built successfully."
echo "Configure forge/src/Forge.Runner/appsettings.json, then run: ./run.sh"
