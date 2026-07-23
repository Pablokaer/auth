#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# CentralLogin setup verification (macOS / zsh / bash).
# Confirms prerequisites and that the solution builds and its tests pass.
# Does NOT deploy anything and does NOT require Keycloak to be running.
# ---------------------------------------------------------------------------
set -euo pipefail
cd "$(dirname "$0")/.."

pass() { printf '  \033[32mOK\033[0m  %s\n' "$1"; }
warn() { printf '  \033[33m!!\033[0m  %s\n' "$1"; }
fail() { printf '  \033[31mXX\033[0m  %s\n' "$1"; exit 1; }

echo "==> Prerequisites"
command -v dotnet >/dev/null || fail "dotnet SDK not found. Install .NET 10 (see global.json)."
SDK="$(dotnet --version)"
case "$SDK" in
  10.*) pass "dotnet SDK $SDK" ;;
  *)    warn "dotnet SDK is $SDK; global.json pins 10.0.302 (rollForward=latestFeature)." ;;
esac
command -v docker >/dev/null && pass "docker present" || warn "docker not found — needed to run Keycloak/Postgres via Aspire."
command -v pulumi >/dev/null && pass "pulumi present" || warn "pulumi not found — needed only for infrastructure deploys."

echo "==> Restore local tools (dotnet-ef, ...)"
dotnet tool restore >/dev/null && pass "tools restored"

echo "==> Build"
dotnet build CentralLogin.sln -c Debug --nologo >/dev/null && pass "solution builds"

echo "==> Tests (unit + integration + architecture)"
dotnet test CentralLogin.sln -c Debug --no-build --nologo >/dev/null && pass "all tests passed"

echo
echo "Verification complete. Next: configure user-secrets, then 'dotnet run --project src/CentralLogin.AppHost'."
