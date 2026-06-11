#!/bin/bash
# Build script for Jellych Jellyfin Plugin - Linux/macOS

set -e

if [ ! -f "Jellyfin.Plugin.Jellych.sln" ]; then
  echo "Error: Run this script from the repository root"
  exit 1
fi

echo "Building Jellych Jellyfin Plugin..."
echo

dotnet clean Jellyfin.Plugin.Jellych.sln -c Release || true
dotnet restore Jellyfin.Plugin.Jellych.sln
dotnet build Jellyfin.Plugin.Jellych.sln -c Release --no-restore

echo
echo "Build succeeded!"
echo
echo "Plugin DLL: src/bin/Release/net8.0/Jellych.WebhookPlugin.dll"
echo
