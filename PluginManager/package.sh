#!/usr/bin/env bash
# Builds PluginManager and packs a ready-to-install zip at ../PluginManager.zip.
set -euo pipefail
cd "$(dirname "$0")"

dotnet build -c Release
out=bin/Release/net8.0-windows
stage=$(mktemp -d)
trap 'rm -rf "$stage"' EXIT

mkdir -p "$stage/PluginManager/PluginManager.bundle/Contents"
cp bundle/PackageContents.xml "$stage/PluginManager/PluginManager.bundle/"
cp "$out/PluginManager.dll" "$out/PluginManager.pdb" "$stage/PluginManager/PluginManager.bundle/Contents/"
cp bundle/install.bat bundle/uninstall.bat "$stage/PluginManager/"
cp bundle/HOW-TO-USE.txt "$stage/PluginManager/"

rm -f ../PluginManager.zip
python3 -c "import shutil,sys; shutil.make_archive(sys.argv[1], 'zip', sys.argv[2], 'PluginManager')" "$(cd .. && pwd)/PluginManager" "$stage"
echo "Created $(cd .. && pwd)/PluginManager.zip"
