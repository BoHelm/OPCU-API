#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
repo_dir=$PWD
mkdir -p .state/demo-run .keys
chmod 700 .state .keys
dotnet restore --locked-mode
dotnet build --no-restore
export DEMO_KEY_FILE="$repo_dir/.state/demo-run/api.key"
dotnet src/DemoApi/bin/Debug/net10.0/DemoApi.dll > .state/demo-run/api.log 2>&1 &
api_pid=$!
gateway_pid=""
cleanup() {
    if [ -n "$gateway_pid" ]; then kill -TERM "$gateway_pid" 2>/dev/null || true; wait "$gateway_pid" || true; fi
    kill -TERM "$api_pid" 2>/dev/null || true
    wait "$api_pid" || true
}
trap cleanup EXIT INT TERM
for attempt in $(seq 1 100); do
    if [ -f "$DEMO_KEY_FILE" ] && curl --fail --silent http://127.0.0.1:5090/health/live >/dev/null; then break; fi
    if ! kill -0 "$api_pid" 2>/dev/null; then echo "Demo-API-Start fehlgeschlagen; siehe .state/demo-run/api.log" >&2; exit 1; fi
    sleep .1
done
export GATEWAY_DEMO=true GATEWAY_DEMO_API_URL=http://127.0.0.1:5090
export GATEWAY_DEMO_KEY_FILE="$DEMO_KEY_FILE"
export GATEWAY_DATA_DIR="$repo_dir/.state/demo-run/gateway"
export GATEWAY_KEY_DIR="$repo_dir/.keys/demo-run"
printf '%s\n' "Lokale Simulation startet. Einrichtungstoken: $GATEWAY_KEY_DIR/bootstrap-token"
cd src/Gateway
dotnet bin/Debug/net10.0/Gateway.dll &
gateway_pid=$!
wait "$gateway_pid"
