# CentralLogin

Centralized authentication platform. A Keycloak realm provides OpenID Connect
identity for one or more ASP.NET Core applications and APIs. The first client is
an ASP.NET Core **Razor Pages** web application. Application-specific data lives
in **SQLite**; identity, credentials and tokens live in **Keycloak** — never in
the application database.

Built with **.NET 10**, **.NET Aspire 13** (local orchestration), **Entity
Framework Core 10**, **Cake Frosting** (build/deploy pipeline), **Pulumi (C#)**
(infrastructure as code), **Docker**, and **GitHub Actions**. 

> Base-architecture scaffold: it builds, its tests pass, and it runs locally
> from the Aspire AppHost. Cloud resources in the Pulumi project are
> provider-neutral extension points and are **not** deployed automatically.

## Table of contents

1. [Technology versions](#technology-versions-pinned)
2. [Solution layout](#solution-layout)
3. [Prerequisites & installation](#prerequisites--installation)
4. [Configuration & secrets](#configuration--secrets)
5. [Local execution](#local-execution)
6. [Ports](#ports)
7. [Keycloak setup](#keycloak-setup)
8. [Testing](#testing)
9. [Database & migrations](#database--migrations)
10. [Cake build pipeline](#cake-build-pipeline)
11. [Pulumi infrastructure](#pulumi-infrastructure)
12. [CI](#ci) · [Deployment](#deployment)
13. [Why SQLite](#why-sqlite)
14. [Security](#security)
15. [Troubleshooting](#troubleshooting)

---

## Technology versions (pinned)

| Component | Version |
|---|---|
| .NET SDK | `10.0.301` (LTS) — `global.json` |
| .NET Aspire | `13.4.6` |
| EF Core (SQLite) | `10.0.10` |
| Keycloak container | `quay.io/keycloak/keycloak:26.5.6` |
| PostgreSQL (Keycloak) | `postgres:17.6` |
| Cake.Frosting | `6.2.0` |
| Pulumi (.NET SDK) | `3.98.0` |

All package versions are centrally pinned in `Directory.Packages.props`.

## Solution layout

```
CentralLogin/
  src/
    CentralLogin.AppHost/          # .NET Aspire orchestrator (dev entry point)
    CentralLogin.ServiceDefaults/  # Telemetry, health checks, resilience, discovery
    CentralLogin.Web/              # Razor Pages: cookie + OIDC auth, policies, security
    CentralLogin.Domain/           # Entities, value objects, domain rules (no infra)
    CentralLogin.Application/      # Use cases, interfaces, commands, validation, authz rules
    CentralLogin.Infrastructure/   # EF Core + SQLite, repositories, migrations
  tests/
    CentralLogin.UnitTests/        # Provisioning + domain rules
    CentralLogin.IntegrationTests/ # SQLite/EF + authorization + optional E2E
    CentralLogin.ArchitectureTests/# Layering rules (NetArchTest)
  build/CentralLogin.Build/        # Cake Frosting pipeline (strongly typed C#)
  infrastructure/CentralLogin.Pulumi/  # Pulumi C# program + stacks (dev/staging/prod)
  keycloak/realm/                  # Development realm import (no real secrets)
  keycloak/themes/central-login/   # Custom login theme (Deep Teal, matches the app)
  .github/workflows/               # Minimal CI/CD YAML delegating to Cake
  scripts/verify.sh                # Setup verification
```

Layer responsibilities: **Domain** is pure .NET (no ASP.NET/EF/Keycloak/Aspire/
Cake/Pulumi — enforced by architecture tests). **Application** holds use cases,
interfaces, commands/queries, validation and application authorization rules,
depending only on Domain. **Infrastructure** owns EF Core, SQLite, repositories
and migrations. **Web** owns Razor Pages, authentication/authorization, security
headers and DI. **AppHost** orchestrates Web + Keycloak + PostgreSQL for local
dev. **ServiceDefaults** centralizes telemetry/health/resilience/discovery.

## Prerequisites & installation

```zsh
# .NET 10 SDK (matches global.json). Homebrew example:
brew install --cask dotnet-sdk
dotnet --version                      # expect 10.0.3xx

# Docker (Keycloak + PostgreSQL run as containers via Aspire)
brew install --cask docker            # then launch Docker Desktop

# Pulumi CLI (only needed for infrastructure work)
brew install pulumi

# Restore local tools (dotnet-ef, etc.) and verify the setup
git clone <your-repo-url> && cd CentralLogin
dotnet tool restore
./scripts/verify.sh
```

Recommended VS Code extensions (offered automatically, not mandatory): C# Dev
Kit, C#, .NET Install Tool, Docker, EditorConfig, Pulumi, YAML — see
`.vscode/extensions.json`.

## Configuration & secrets

No secrets are committed. Local secrets come from **.NET user-secrets** and
**Aspire parameters**. Copy `.env.example` to `.env` only for shell convenience;
`.env` is gitignored.

Set the AppHost secrets (never hardcoded in source):

```zsh
dotnet user-secrets --project src/CentralLogin.AppHost set Parameters:keycloak-admin-username admin
dotnet user-secrets --project src/CentralLogin.AppHost set Parameters:keycloak-admin-password "$(openssl rand -base64 24)"
dotnet user-secrets --project src/CentralLogin.AppHost set Parameters:keycloak-db-password "$(openssl rand -base64 24)"
dotnet user-secrets --project src/CentralLogin.AppHost set Parameters:central-login-web-client-secret "$(openssl rand -base64 32)"

# The web app needs the SAME client secret it will present to Keycloak:
dotnet user-secrets --project src/CentralLogin.Web set "Keycloak:ClientSecret" "<same value as above>"
```

Non-secret settings live in `src/CentralLogin.Web/appsettings*.json`
(`Keycloak:Authority`, `Keycloak:ClientId`, `Database:*`). In staging/production
supply secrets via environment variables or your cloud secret manager.

## Local execution

> The commands below are shown for macOS/zsh. On **Windows** use PowerShell —
> the `dotnet` commands are identical; only the shell-specific bits differ
> (e.g. `docker start` instead of `$(openssl ...)` substitutions).

### A. Intended way — Aspire AppHost (one command)

Everything starts from the Aspire AppHost:

```zsh
dotnet run --project src/CentralLogin.AppHost
```

This brings up PostgreSQL, Keycloak (importing the `central-login` realm) and the
web app, and opens the **Aspire dashboard** (logs, traces, health, service
status). The web app waits for Keycloak to become healthy before starting.

First run only — set the Keycloak client secret and dev-admin password (see
[Keycloak setup](#keycloak-setup)). In VS Code, use the **Run AppHost (Aspire)**
launch configuration.

> **Known issue (see [Troubleshooting](#troubleshooting)).** The AppHost pins the
> Keycloak *management* endpoint to host port `9000`, which collides with the
> container's own published port and makes the DCP health-check proxy fail to
> bind — so `WaitFor(keycloak)` never releases and the web app never starts.
> The realm import file also currently contains an invalid `_comment_credentials`
> field that crashes `--import-realm`. Until both are fixed, use option **B**.

### B. Reliable fallback — containers + the web app directly

Run Keycloak and PostgreSQL as plain Docker containers and start only the web
app. This bypasses the Aspire orchestrator entirely.

```zsh
# 1) A user-defined network so Keycloak can reach PostgreSQL by name
docker network create centrallogin-net

# 2) PostgreSQL (Keycloak's database) — data lives in a named volume
docker run -d --name centrallogin-postgres --network centrallogin-net \
  -e POSTGRES_PASSWORD='<db password>' -e POSTGRES_DB=keycloak \
  -v centrallogin-keycloak-pgdata:/var/lib/postgresql/data \
  -p 15432:5432 postgres:17.6

# 3) Keycloak (dev mode). Mount the custom login theme; import is skipped
#    once the realm already exists in the database volume.
docker run -d --name centrallogin-keycloak --network centrallogin-net \
  -e KC_BOOTSTRAP_ADMIN_USERNAME=admin -e KC_BOOTSTRAP_ADMIN_PASSWORD='<admin pw>' \
  -e KC_HEALTH_ENABLED=true -e KC_METRICS_ENABLED=true \
  -e KC_DB=postgres -e KC_DB_USERNAME=postgres -e KC_DB_PASSWORD='<db password>' \
  -e KC_DB_URL='jdbc:postgresql://centrallogin-postgres:5432/keycloak' \
  -v "$PWD/keycloak/themes:/opt/keycloak/themes" \
  -p 8080:8080 -p 9000:9000 \
  quay.io/keycloak/keycloak:26.5.6 start-dev

# 4) The web app (reads Keycloak:Authority from appsettings.Development.json)
dotnet run --project src/CentralLogin.Web --launch-profile http
```

On subsequent sessions the containers already exist — just `docker start
centrallogin-postgres centrallogin-keycloak` and re-run step 4. The web app is
then served at **http://localhost:5080** (see [Ports](#ports)).

## Ports

| Service | Port(s) | URL / notes |
|---|---|---|
| **CentralLogin.Web** (the app) | `5080` (HTTP), `7080` (HTTPS) | http://localhost:5080 — user entry point. Must match the realm redirect URIs. |
| **Keycloak** — main HTTP | `8080` | http://localhost:8080 — realm, OIDC authority, admin console (`/admin`). |
| **Keycloak** — management | `9000` | Health (`/health/ready`) and metrics. Internal use. |
| **PostgreSQL** (Keycloak DB) | `15432` → container `5432` | Used only by Keycloak; the app never connects to it. |
| **Aspire dashboard** | dynamic (e.g. `https://localhost:5xxxx`) | Dev-only observability UI; only when running via the AppHost. |

The application database is **SQLite** — a local file at
`src/CentralLogin.Web/data/centrallogin.db`, not a network port.

## Keycloak setup

The realm import (`keycloak/realm/central-login-realm.json`) defines the
`central-login` realm, the confidential `central-login-web` client (Authorization
Code + PKCE, exact dev redirect and post-logout URIs), the `user` and
`administrator` realm roles, and a `dev-admin` user **with no password**. It
contains **no secrets**.

After the first `aspire run`, configure the client secret and dev-admin password
(values read from your environment, never stored in the repo):

```zsh
export KEYCLOAK_URL=http://localhost:8080
export KEYCLOAK_ADMIN_USERNAME=admin KEYCLOAK_ADMIN_PASSWORD='<your admin pw>'
export CENTRALLOGIN_WEB_CLIENT_SECRET='<same secret as user-secrets>'
export KEYCLOAK_DEV_ADMIN_PASSWORD="$(openssl rand -base64 24)"
./keycloak/configure-dev.sh
```

See `keycloak/README.md` for dev-vs-production details, `keycloak.conf.example`
for the production variables (hostname, proxy headers, HTTPS, health, metrics,
database, bootstrap admin), and how to **rotate the client secret**. Production
runs `start` (never `start-dev`) against PostgreSQL, behind a proxy with HTTPS.

### Login theme

A custom login theme lives in `keycloak/themes/central-login/` and matches the
web app's Deep Teal design system. To use it, the themes directory must be
mounted into the container at `/opt/keycloak/themes` (see the `-v` flag in
[Local execution → B](#local-execution)) and the realm's **Login theme** set to
`central-login` (Realm settings → Themes in the admin console, or a
`"loginTheme": "central-login"` entry on the realm). Only colours/styling change;
edit `keycloak/themes/central-login/login/resources/css/custom.css`. Under
`start-dev` theme caching is off, so a browser refresh shows edits immediately.

## Testing

```zsh
dotnet test                                   # unit + integration + architecture
dotnet test tests/CentralLogin.UnitTests
dotnet test tests/CentralLogin.IntegrationTests
dotnet test tests/CentralLogin.ArchitectureTests

# Optional end-to-end profile (requires Docker; runs the full Aspire env):
dotnet test tests/CentralLogin.IntegrationTests -p:E2E=true
```

Unit tests cover local user provisioning and domain rules. Integration tests
cover SQLite/EF Core (real migrated database) and authorization for
authenticated users, unauthenticated users, administrators and users without the
required role — using a **test authentication handler** so no live Keycloak is
needed. The architecture tests enforce the layering rules.

## Database & migrations

SQLite lives in a configurable persistent directory (`Database:DataDirectory`,
default `./data`), with WAL journaling, `synchronous=NORMAL`, a busy timeout and
foreign keys enabled, and a unique index on `KeycloakSubject`. Migrations are
applied automatically at startup.

```zsh
# Add a migration after changing the model:
dotnet tool run dotnet-ef migrations add <Name> \
  --project src/CentralLogin.Infrastructure \
  --output-dir Persistence/Migrations

# Apply migrations manually (normally automatic at startup):
dotnet tool run dotnet-ef database update --project src/CentralLogin.Infrastructure
```

## Cake build pipeline

Strongly-typed C# tasks in `build/CentralLogin.Build`:

```zsh
dotnet run --project build/CentralLogin.Build -- --target=CI          # format, vuln scan, build, all tests
dotnet run --project build/CentralLogin.Build -- --target=Build
dotnet run --project build/CentralLogin.Build -- --target=Publish
dotnet run --project build/CentralLogin.Build -- --target=DockerBuild
dotnet run --project build/CentralLogin.Build -- --target=PulumiPreview --environment=Staging
```

Tasks: `Clean, Restore, FormatCheck, Build, UnitTests, IntegrationTests,
ArchitectureTests, Publish, DockerBuild, PulumiPreview, PulumiDeploy, CI, CD`
(plus a `VulnerabilityCheck`). `CI` runs all validation/tests. `CD` depends on
`CI` (so it cannot deploy when tests fail) and additionally publishes, builds the
container and runs Pulumi. `PulumiDeploy` requires an explicit `--environment`
and refuses Production without `--confirm-production=true`. Pulumi is invoked
through an isolated `IPulumiRunner` (CLI today, Automation API later).

## Pulumi infrastructure

Provider-neutral scaffold with separate `dev`/`staging`/`prod` stacks. It
prepares abstractions for application hosting, Keycloak hosting, PostgreSQL,
persistent storage, container registry, DNS, TLS, secret management, logging,
monitoring and backups. Sensitive config is marked secret; only non-sensitive
outputs are exported. See `infrastructure/CentralLogin.Pulumi/README.md` for
`pulumi preview` / `up` / `refresh` / `destroy` and provider extension points.
No resources are created until a provider is selected.

## CI

`.github/workflows/ci.yml` runs on pull requests and pushes to `main`: checkout,
install the pinned SDK (`global-json-file`), `dotnet tool restore`, then the Cake
`CI` target. No build logic lives in YAML.

## Deployment

`.github/workflows/cd.yml` is separate and **manual only** (`workflow_dispatch`
with a `staging`/`production` choice), so untrusted PRs can never trigger a
deploy. It uses GitHub **environments** for protection — configure the
`production` environment with required reviewers to enforce **manual approval**.
Secrets (e.g. `PULUMI_ACCESS_TOKEN`, cloud credentials) come from environment
secrets / OIDC federation. The job delegates to the Cake `CD` target, which runs
`CI` first and passes `--confirm-production=true` only for production.

## Why SQLite

SQLite stores only application-specific data (the local `ApplicationUser`
projection keyed by the Keycloak subject). It is ideal for the initial
single-instance deployment: zero-ops, file-based, transactional. **Replace it
with PostgreSQL** when you need horizontal scaling (multiple app instances
writing concurrently) or significant concurrent write load — SQLite allows only
one writer at a time. The data layer sits behind repository interfaces in the
Application layer, so the provider can be swapped in Infrastructure without
touching domain or web code.

## Security

- **No secrets in source control.** Credentials, password hashes, refresh
  tokens, client secrets and Keycloak-internal data are never stored in SQLite or
  committed. Secrets flow via user-secrets / Aspire parameters (dev) and
  environment/secret manager (staging/prod). `appsettings*.json` hold no secrets.
- **Token handling.** Standard ASP.NET Core OIDC middleware validates issuer,
  audience, lifetime and signing keys, and handles state/nonce/PKCE. We never
  parse or validate JWT signatures manually. Tokens, authorization codes, cookies
  and secrets are never logged (the provisioning log emits only a subject hash).
- **Cookies.** HttpOnly, `SameSite=Lax`, Secure outside development (`__Host-`
  prefix in non-dev). HTTPS + HSTS enforced outside development.
- **CSRF.** Razor Pages antiforgery protects state-changing (POST) requests,
  including logout.
- **Headers & CSP.** `SecurityHeadersMiddleware` sets a Razor-appropriate CSP
  (no inline/eval scripts), `X-Content-Type-Options`, `X-Frame-Options: DENY`,
  `Referrer-Policy`, `Cross-Origin-Opener-Policy` and `Permissions-Policy`.
- **Rate limiting** is applied to authentication-initiating endpoints.
- **Dependency scanning** runs in CI (`dotnet list package --vulnerable` via the
  Cake `VulnerabilityCheck` task).
- **Keycloak admin console** must not be exposed publicly without restrictions;
  production uses exact redirect URIs and HTTPS.
- **No production admins are created at app startup.** The `dev-admin` user
  exists only in the development realm import, without a committed password.
- **Secret rotation.** Rotate the client secret in the Keycloak admin console
  (Clients → central-login-web → Credentials → Regenerate) or via `kcadm.sh`, then
  update `Keycloak:ClientSecret` in the app's secret store. Rotate on a schedule
  and after any suspected exposure. See `keycloak/README.md`.
- **Backup & restore.** SQLite: back up the database file and its `-wal`/`-shm`
  siblings (or use `sqlite3 .backup`) from the persistent data directory; restore
  by replacing the files while the app is stopped. Keycloak (PostgreSQL): use
  managed PostgreSQL backups / `pg_dump`; the Pulumi `BackupPlan` prepares
  retention settings per environment. Owning team: whoever operates the
  deployment is responsible for scheduling and testing restores.

## Troubleshooting

- **Web app never starts under the Aspire AppHost (port `5080` hangs / times
  out)** — the AppHost pins the Keycloak *management* endpoint to host port
  `9000` (`AppHost.cs`: `WithHttpEndpoint(port: 9000, targetPort: 9000, name:
  "management")`). DCP cannot bind its health-check proxy on an already-published
  `9000`, so Keycloak is never reported healthy, `WaitFor(keycloak)` never
  releases, and the web app is never launched. Fix by not pinning the management
  host port: `WithHttpEndpoint(targetPort: 9000, name: "management")`. Meanwhile,
  run via [Local execution → B](#local-execution).
- **Keycloak container exits immediately with `Unrecognized field
  "_comment_credentials"`** — the realm import file
  (`keycloak/realm/central-login-realm.json`) contains a non-standard
  `_comment_credentials` field that `--import-realm` rejects. Remove that field,
  or run Keycloak without `--import-realm` once the realm already exists in the
  PostgreSQL volume.
