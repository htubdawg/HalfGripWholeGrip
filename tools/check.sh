#!/usr/bin/env bash
# Uses HalfGripRefs' compile checker against an isolated loader copy: reference repos stay untouched.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
REFS=${HALFGRIP_REFS:-/workspace/HalfGripRefs}
LOADER=${HALFGRIP_TOOLS:-/workspace/setup-halfgrip/dependencies/HalfGripTools}
BUILD="$ROOT/work/check"
mkdir -p "$BUILD/loader"
cp -R "$LOADER/HalfGripTools" "$BUILD/loader/"
# Override the cached SDK/package directories if the caller already configured dotnet.
if ! command -v dotnet >/dev/null && [ -f /workspace/setup-halfgrip/activate.sh ]; then
    source /workspace/setup-halfgrip/activate.sh
fi
export TMPDIR="$BUILD"
rm -f "$BUILD/halfgrip-check/out/HalfGripMod.Check.dll"
bash "$REFS/tools/check-mod.sh" "$ROOT/src" "$BUILD/loader"
# check-mod.sh filters compiler output through a pipeline; assert the output exists as well.
test -s "$BUILD/halfgrip-check/out/HalfGripMod.Check.dll"
dotnet run --project "$ROOT/tests/WholeGrip.Checks.csproj" -c Release
