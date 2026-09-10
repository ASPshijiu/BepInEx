#!/bin/sh
set -eu
dobby_root=$(cd "${1:?Usage: run-dobby-tests.sh DOBBY_SOURCE [LIBDOBBY]}" && pwd)
native_library=${2:-$dobby_root/build-macos-x64/libdobby.dylib}
test_root=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
test_tmp=$(mktemp -d)
trap 'rm -rf "$test_tmp"' EXIT
clang -arch x86_64 -O0 "$test_root/dobby-rosetta.c" -o "$test_tmp/rosetta"
"$test_tmp/rosetta" "$native_library"
clang++ -arch x86_64 -std=c++11 -DNDEBUG \
  -I"$dobby_root" -I"$dobby_root/include" -I"$dobby_root/source" \
  -I"$dobby_root/source/include" -I"$dobby_root/source/UserMode" \
  -I"$dobby_root/external" -I"$dobby_root/external/logging" \
  -I"$dobby_root/external/xnucxx" -I"$dobby_root/external/misc-helper" \
  "$test_root/dobby-cave-allocation.cc" \
  "$dobby_root/source/MemoryAllocator/NearMemoryArena.cc" \
  "$dobby_root"/external/xnucxx/*.cc -o "$test_tmp/caves"
"$test_tmp/caves"
