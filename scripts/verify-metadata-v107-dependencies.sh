#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$repo_root/Runtimes/Unity/BepInEx.Unity.IL2CPP/BepInEx.Unity.IL2CPP.csproj"
lock_file="$repo_root/Runtimes/Unity/BepInEx.Unity.IL2CPP/packages.lock.json"
dotnet_cmd="${DOTNET_CMD:-dotnet}"

if [[ ! -f "$lock_file" ]]; then
    printf 'FAIL: missing %s.\n' "$lock_file" >&2
    exit 1
fi

"$dotnet_cmd" restore "$project" --locked-mode --no-cache
package_output="$($dotnet_cmd list "$project" package --include-transitive)"

require_package() {
    local package_name="$1"
    local package_version="$2"
    if ! printf '%s\n' "$package_output" | grep -F "$package_name" | grep -Fq "$package_version"; then
        printf 'FAIL: expected %s %s in resolved package graph.\n' "$package_name" "$package_version" >&2
        exit 1
    fi
}

require_package "Samboy063.Cpp2IL.Core" "2022.1.0-development.1715"
require_package "Il2CppInterop.Generator" "1.5.3-ci.1121"
require_package "Il2CppInterop.HarmonySupport" "1.5.3-ci.1121"
require_package "Il2CppInterop.Runtime" "1.5.3-ci.1121"
require_package "Il2CppInterop.ReferenceLibs" "1.0.0"

printf 'PASS: metadata v107 dependency contract is locked and restorable.\n'
