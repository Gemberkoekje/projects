#!/bin/sh
# Writes runtime configuration into /usr/share/nginx/html/config.js so that
# secret values are injected at container startup rather than baked into the
# static bundle at build time.
#
# Environment variables consumed:
#   DASHBOARD_API_KEY   — dashboard-scoped read-only API key (required in prod)
#   API_BASE_URL        — override for the API base URL (optional)

API_BASE_URL="${API_BASE_URL:-/spacetraders/api}"
DASHBOARD_API_KEY="${DASHBOARD_API_KEY:-}"

# Escape values for safe embedding in a JSON string:
# replace \ with \\, then " with \", then newlines with \n
json_escape() {
  printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g' | tr -d '\n'
}

ESCAPED_API_BASE_URL="$(json_escape "$API_BASE_URL")"
ESCAPED_DASHBOARD_API_KEY="$(json_escape "$DASHBOARD_API_KEY")"

printf 'window.__RUNTIME_CONFIG__ = { "apiBaseUrl": "%s", "dashboardApiKey": "%s" };\n' \
  "$ESCAPED_API_BASE_URL" "$ESCAPED_DASHBOARD_API_KEY" \
  > /usr/share/nginx/html/config.js

echo "Runtime config written to /usr/share/nginx/html/config.js"
