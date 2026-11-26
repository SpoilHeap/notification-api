#!/usr/bin/env bash
# Smoke-test a built image: start it and check the endpoints the add-on depends on.
# Usage: scripts/smoke-test-image.sh <image>
set -euo pipefail

IMAGE="${1:?usage: $0 <image>}"
NAME="notification-api-smoke-$$"
PORT="${SMOKE_PORT:-18080}"
BASE="http://localhost:$PORT"

cleanup() {
  docker logs "$NAME" >/tmp/"$NAME".log 2>&1 || true
  docker rm -f "$NAME" >/dev/null 2>&1 || true
}
trap cleanup EXIT

docker run -d --name "$NAME" -p "$PORT:8080" "$IMAGE" >/dev/null

for _ in $(seq 1 30); do
  curl -fsS "$BASE/health" >/dev/null 2>&1 && break
  sleep 1
done

fail() { echo "✗ $*" >&2; echo "--- container log ---" >&2; docker logs "$NAME" >&2 || true; exit 1; }

curl -fsS "$BASE/health" >/dev/null || fail "/health did not return 2xx"
echo "✓ /health"

html="$(curl -fsS "$BASE/")" || fail "/ did not return 2xx"
echo "✓ / renders"

script="$(grep -oE '_framework/blazor\.web(\.[a-z0-9]+)?\.js' <<<"$html" | head -1)"
[[ -n "$script" ]] || fail "page has no blazor.web.js script tag"

# The spike regression: a bad restore drops blazor.web.js from the publish output (404).
read -r status ctype < <(curl -s -o /dev/null -w '%{http_code} %{content_type}\n' "$BASE/$script")
[[ "$status" == "200" ]] || fail "$script returned $status"
[[ "$ctype" == text/javascript* ]] || fail "$script served as '$ctype'"
echo "✓ $script ($ctype)"

echo "smoke test passed: $IMAGE"
