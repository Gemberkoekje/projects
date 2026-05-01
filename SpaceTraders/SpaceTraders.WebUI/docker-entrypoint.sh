#!/bin/sh
# Writes runtime configuration into /usr/share/nginx/html/config.js so that
# secret values are injected at container startup rather than baked into the
# static bundle at build time.
#
# Environment variables consumed:
#   SPACETRADERS_INTERNAL_API_KEY — internal API key used by both API and WebUI
#   DASHBOARD_API_KEY   — legacy override for the dashboard API key (optional)
#   API_BASE_URL        — override for the API base URL (optional)

API_BASE_URL="${API_BASE_URL:-/spacetraders/api}"
# Prefer the shared API key so only one secret key is required in Kubernetes.
# Keep DASHBOARD_API_KEY as an optional override for backward compatibility.
DASHBOARD_API_KEY="${DASHBOARD_API_KEY:-${SPACETRADERS_INTERNAL_API_KEY:-}}"
HUB_BASE_URL="${HUB_BASE_URL:-/spacetraders/api/hubs}"

# Escape values for safe embedding in a JSON string:
# replace \ with \\, then " with \", then newlines with \n
json_escape() {
  printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g' | tr -d '\n'
}

ESCAPED_API_BASE_URL="$(json_escape "$API_BASE_URL")"
ESCAPED_DASHBOARD_API_KEY="$(json_escape "$DASHBOARD_API_KEY")"
ESCAPED_HUB_BASE_URL="$(json_escape "$HUB_BASE_URL")"

printf 'window.__RUNTIME_CONFIG__ = { "apiBaseUrl": "%s", "dashboardApiKey": "%s", "hubBaseUrl": "%s" };\n' \
  "$ESCAPED_API_BASE_URL" "$ESCAPED_DASHBOARD_API_KEY" "$ESCAPED_HUB_BASE_URL" \
  > /usr/share/nginx/html/config.js

echo "Runtime config written to /usr/share/nginx/html/config.js"
