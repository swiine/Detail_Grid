#!/usr/bin/env bash
# Builds the launcher and the example plugin, lays them out in ready-to-copy/Plugins exactly as
# they go on A:, and zips everything (source included) to ../TTW.PluginTools.zip.
set -euo pipefail
cd "$(dirname "$0")"

dotnet build src/TTW.PluginTools -c Release
dotnet build example/TTW.PluginTools.Example -c Release

launcher=src/TTW.PluginTools/bin/Release/net8.0-windows
example=example/TTW.PluginTools.Example/bin/Release/net8.0-windows
out=ready-to-copy/Plugins

rm -rf ready-to-copy
mkdir -p "$out/TTW.PluginTools" "$out/TTW.PluginTools.Example"
cp "$launcher/TTW.PluginTools.dll" "$launcher/System.Reflection.MetadataLoadContext.dll" "$launcher/TTW.PluginTools.config.json" \
   deploy/TTW_Plugin_Tools_Loader.lsp "$out/TTW.PluginTools/"
cp "$example/TTW.PluginTools.Example.dll" "$example/TTWEXAMPLEREPORT.txt" "$out/TTW.PluginTools.Example/"

stage=$(mktemp -d)
trap 'rm -rf "$stage"' EXIT
mkdir "$stage/TTW.PluginTools"
cp -r README.md deploy ready-to-copy "$stage/TTW.PluginTools/"
cp -r src example "$stage/TTW.PluginTools/"
find "$stage" -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +

rm -f ../TTW.PluginTools.zip
python3 -c "import shutil,sys; shutil.make_archive(sys.argv[1], 'zip', sys.argv[2], 'TTW.PluginTools')" "$(cd .. && pwd)/TTW.PluginTools" "$stage"
echo "Created $(cd .. && pwd)/TTW.PluginTools.zip"
