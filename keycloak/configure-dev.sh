#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# One-time local Keycloak configuration for CentralLogin.
#
# Runs AFTER Keycloak has started and imported the central-login realm. It:
#   1. sets the confidential client secret for `central-login-web` to the value
#      of $CENTRALLOGIN_WEB_CLIENT_SECRET (so it matches the web app's secret), and
#   2. sets the dev-admin user's password to $KEYCLOAK_DEV_ADMIN_PASSWORD.
#
# No secret or password is ever stored in the repository — both values are read
# from the environment (populate them via .env / user-secrets / Aspire params).
#
# Usage (zsh/bash, macOS):
#   export KEYCLOAK_URL=http://localhost:8080
#   export KEYCLOAK_ADMIN_USERNAME=... KEYCLOAK_ADMIN_PASSWORD=...
#   export CENTRALLOGIN_WEB_CLIENT_SECRET=... KEYCLOAK_DEV_ADMIN_PASSWORD=...
#   ./keycloak/configure-dev.sh
# ---------------------------------------------------------------------------
set -euo pipefail

KEYCLOAK_URL="${KEYCLOAK_URL:-http://localhost:8080}"
REALM="central-login"
CLIENT_ID="central-login-web"

: "${KEYCLOAK_ADMIN_USERNAME:?set KEYCLOAK_ADMIN_USERNAME}"
: "${KEYCLOAK_ADMIN_PASSWORD:?set KEYCLOAK_ADMIN_PASSWORD}"
: "${CENTRALLOGIN_WEB_CLIENT_SECRET:?set CENTRALLOGIN_WEB_CLIENT_SECRET}"
: "${KEYCLOAK_DEV_ADMIN_PASSWORD:?set KEYCLOAK_DEV_ADMIN_PASSWORD}"

# Locate kcadm.sh inside the running Keycloak container (adjust name if needed).
KC_CONTAINER="${KC_CONTAINER:-$(docker ps --filter "ancestor=quay.io/keycloak/keycloak:26.5.6" --format '{{.Names}}' | head -1)}"
if [[ -z "${KC_CONTAINER}" ]]; then
  echo "Could not find a running Keycloak container. Is Aspire up?" >&2
  exit 1
fi

kc() { docker exec -i "${KC_CONTAINER}" /opt/keycloak/bin/kcadm.sh "$@"; }

echo "Authenticating to ${KEYCLOAK_URL} ..."
kc config credentials --server "${KEYCLOAK_URL}" --realm master \
  --user "${KEYCLOAK_ADMIN_USERNAME}" --password "${KEYCLOAK_ADMIN_PASSWORD}"

echo "Setting client secret for ${CLIENT_ID} ..."
CID=$(kc get clients -r "${REALM}" -q clientId="${CLIENT_ID}" --fields id --format csv --noquotes | tail -1)
kc update "clients/${CID}" -r "${REALM}" -s "secret=${CENTRALLOGIN_WEB_CLIENT_SECRET}"

echo "Setting dev-admin password ..."
kc set-password -r "${REALM}" --username dev-admin --new-password "${KEYCLOAK_DEV_ADMIN_PASSWORD}"

echo "Done. central-login-web secret and dev-admin password are configured."
