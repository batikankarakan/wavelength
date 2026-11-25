#!/usr/bin/env bash
# Compile check on macOS/Linux (no Windows needed): generates C# from all Razor views, then compiles
# controllers + models + services + views against .NET Framework 4.8 with C# 7.3 (what Visual Studio uses).
# Usage: tools/check.sh [name]   (a name keeps parallel runs apart)
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_TELEMETRY_OPTOUT=1
NAME=${1:-main}
VIEWS="views-$NAME"
RAZOR_OK=1
dotnet run --project tools/razor-gen -- Wavelength.Web "tools/compile-check/$VIEWS" || RAZOR_OK=0
[ -f tools/compile-check/obj/project.assets.json ] || dotnet restore tools/compile-check >/dev/null
dotnet build tools/compile-check --no-restore -nologo -v q -p:ViewsDir="$VIEWS" \
  -p:IntermediateOutputPath="obj/$NAME/" -p:OutputPath="bin/$NAME/" 2>&1 \
  | grep -E "error|warning CS|Build succeeded" | sed "s|$PWD/||g; s| \[tools/compile-check/CompileCheck.csproj\]||" | sort -u
if [ "$RAZOR_OK" = 0 ]; then echo "FAILED: Razor parser errors above"; exit 1; fi
