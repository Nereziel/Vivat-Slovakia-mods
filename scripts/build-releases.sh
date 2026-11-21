#!/usr/bin/env bash
set -euo pipefail

if [[ -z "${BEPINEX_DIR:-}" ]]; then
  echo "BEPINEX_DIR must point to BepInEx with core and interop directories." >&2
  exit 1
fi
if [[ ! -f "$BEPINEX_DIR/core/BepInEx.Core.dll" || ! -f "$BEPINEX_DIR/interop/Assembly-CSharp.dll" ]]; then
  echo "Required BepInEx/core and BepInEx/interop references not found." >&2
  exit 1
fi

mkdir -p dist
: > dist/manifest.txt
shopt -s nullglob
projects=(Vivat*/*.csproj)
if [[ ${#projects[@]} -eq 0 ]]; then
  echo "No mod projects were found." >&2
  exit 1
fi

for project in "${projects[@]}"; do
  directory="$(dirname "$project")"
  module="$(basename "$directory")"
  assembly="$(sed -n 's/.*<AssemblyName>\([^<]*\)<\/AssemblyName>.*/\1/p' "$project" | head -n 1)"
  assembly="${assembly:-$module}"
  version="$(grep -h 'BepInPlugin(' "$directory"/*.cs | sed -nE 's/.*"([0-9]+\.[0-9]+\.[0-9]+)"\)\].*/\1/p' | head -n 1)"
  if [[ -z "$version" ]]; then
    echo "No BepInPlugin version found in $directory" >&2
    exit 1
  fi

  echo "Building $module v$version ($assembly)"
  dotnet build "$project" -c Release "-p:BepInExDir=$BEPINEX_DIR"
  dll="$directory/bin/Release/net6.0/$assembly.dll"
  if [[ ! -f "$dll" ]]; then
    echo "Compiled DLL missing: $dll" >&2
    exit 1
  fi

  stage="$(mktemp -d)"
  mkdir -p "$stage/BepInEx/plugins/$module"
  cp "$dll" "$stage/BepInEx/plugins/$module/$assembly.dll"

  if [[ "$module" == "VivatPolice" ]]; then
    for voice in charon fenrir; do
      mkdir -p "$stage/BepInEx/plugins/VivatPoliceVoices/$voice"
      cp "$directory/PoliceVoiceSamples/Relay/$voice/"*.wav \
        "$stage/BepInEx/plugins/VivatPoliceVoices/$voice/"
    done
  fi

  archive="dist/$module-v$version.zip"
  (cd "$stage" && zip -q -r "$OLDPWD/$archive" BepInEx)
  rm -rf "$stage"
  printf '%s|%s|%s|%s\n' "$module" "$version" "$module-v$version" "$archive" >> dist/manifest.txt
  echo "Packaged $archive"
done
