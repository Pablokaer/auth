# CentralLogin

Centralized authentication platform. A Keycloak realm provides OpenID Connect
identity for one or more ASP.NET Core applications and APIs. The first client is
an ASP.NET Core **Razor Pages** web application. Application-specific data lives
in **SQLite locally, PostgreSQL in deployed environments**; identity,
credentials and tokens live in **Keycloak** — never in the application
database.

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
8. [Portals](#portals)
9. [Test accounts (local development)](#test-accounts-local-development)
10. [Testing](#testing)
11. [Database & migrations](#database--migrations)
12. [Data Protection key ring](#data-protection-key-ring)
13. [Cake build pipeline](#cake-build-pipeline)
14. [Pulumi infrastructure](#pulumi-infrastructure)
15. [CI](#ci) · [Deployment](#deployment)
16. [Why SQLite locally, PostgreSQL in production](#why-sqlite-locally-postgresql-in-production)
17. [Security](#security)
18. [Troubleshooting](#troubleshooting)

---

## Technology versions (pinned)

| Component | Version |
|---|---|
| .NET SDK | `10.0.301` (LTS) — `global.json` |
| .NET Aspire | `13.4.6` |
| EF Core (SQLite) | `10.0.10` |
| EF Core (PostgreSQL, Npgsql) | `10.0.3` |
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
    CentralLogin.Infrastructure/   # EF Core, repositories, DatabaseOptions, SQLite migrations
    CentralLogin.Infrastructure.Postgres/ # PostgreSQL migration set only (separate migrations assembly)
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
depending only on Domain. **Infrastructure** owns EF Core, the SQLite and
PostgreSQL providers, repositories, `DatabaseOptions` and the SQLite migrations.
**Infrastructure.Postgres** exists solely so the PostgreSQL migrations live in
their own assembly (EF Core scans a whole assembly for migrations, and the two
providers' migrations must never mix). **Web** owns Razor Pages,
authentication/authorization, security headers and DI. **AppHost** orchestrates
Web + Keycloak + PostgreSQL for local dev. **ServiceDefaults** centralizes
telemetry/health/resilience/discovery.

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

The `Keycloak` section is validated at **startup** (`ValidateOnStart`): a missing
`Authority`/`ClientSecret`/`ClientId`, an `Authority` still set to the
`REPLACE_WITH_KEYCLOAK_HOST` placeholder shipped in `appsettings.json`, or plain
http / `RequireHttpsMetadata: false` outside Development aborts the host with a
message naming the offending key. The app never starts half-configured, so a
deployment mistake is caught by your rollout rather than by the first user to
sign in.

The `Database` section is validated the same way (`DatabaseOptionsValidator` +
`ValidateOnStart`): `Database:Provider` selects `Sqlite` (default, requires
`DataDirectory`/`FileName`) or `Postgres` (requires `ConnectionString`); the
host aborts at startup rather than failing on the first query if the selected
provider is missing what it needs. Never commit a real
`Database:ConnectionString` — supply it via user-secrets locally or a Container
App secret in deployed environments (see
[Pulumi infrastructure](#pulumi-infrastructure)).

## Local execution

> **TL;DR — day-to-day run.** Once the Keycloak + PostgreSQL containers exist and
> are up (`docker start centrallogin-postgres centrallogin-keycloak`), starting
> the app is a single command:
>
> ```zsh
> dotnet run --project src/CentralLogin.Web --launch-profile http
> ```
>
> The app is then served at **http://localhost:5080**. Sign in with the
> [test accounts](#test-accounts-local-development). Full first-time container
> setup is in [option B](#b-reliable-fallback--containers--the-web-app-directly)
> below.

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

The application database is **SQLite** locally — a local file at
`src/CentralLogin.Web/data/centrallogin.db`, not a network port. It is generated
automatically on first run (see [Database & migrations](#database--migrations))
and is **not checked into git** — the database file and its WAL/SHM siblings are
gitignored. Deployed environments instead point the app at the PostgreSQL
Flexible Server that Pulumi provisions (see
[Pulumi infrastructure](#pulumi-infrastructure)) — never at a container-local
file, since every replica must share the same store.

## Keycloak setup

The realm import (`keycloak/realm/central-login-realm.json`) defines the
`central-login` realm, the confidential `central-login-web` client (Authorization
Code + PKCE, exact dev redirect and post-logout URIs), the `user`,
`administrator` and `employee` realm roles, and five local test users —
`dev-admin`, `dev-user`, `central.portal`, `employee.central` and
`admin.portal` — **none of which have a password**. It contains **no secrets**
(enforced by a test that scans `keycloak/` and fails the build if a password or
secret value is reintroduced).

After the first `aspire run`, configure the client secret and the local test
user passwords (values read from your environment, never stored in the repo):

```zsh
export KEYCLOAK_URL=http://localhost:8080
export KEYCLOAK_ADMIN_USERNAME=admin KEYCLOAK_ADMIN_PASSWORD='<your admin pw>'
export CENTRALLOGIN_WEB_CLIENT_SECRET='<same secret as user-secrets>'
export KEYCLOAK_DEV_ADMIN_PASSWORD="$(openssl rand -base64 24)"
export KEYCLOAK_CENTRAL_PORTAL_PASSWORD="$(openssl rand -base64 24)"
export KEYCLOAK_EMPLOYEE_CENTRAL_PASSWORD="$(openssl rand -base64 24)"
export KEYCLOAK_ADMIN_PORTAL_PASSWORD="$(openssl rand -base64 24)"
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

## Portals

CentralLogin is the **single sign-in entry point**; the portals are the separate
applications an account actually works in. Sign-in always happens here, against
the `central-login` Keycloak realm, and each account is then *homed* to exactly
one portal chosen by its **realm role**.

### How routing works

1. The account signs in at CentralLogin.Web (or is redirected here by a portal
   that requires authentication).
2. On a successful ticket (`OnTicketReceived` in
   `src/CentralLogin.Web/Authentication/AuthenticationSetup.cs`),
   [`PortalRouter`](src/CentralLogin.Web/Authentication/PortalRouter.cs) reads
   the realm roles straight from the validated `realm_access` claim and returns
   the base URL of the matching portal.
3. That URL replaces `context.ReturnUri`, so the browser lands on the portal,
   which completes **its own** OIDC login silently over the shared Keycloak SSO
   session — the user never types credentials twice.

| Realm role | Configuration key | Local dev URL | Keycloak client |
|---|---|---|---|
| `administrator` | `Portals:AdminUrl` | http://localhost:5100 | `admin-portal` |
| `employee` | `Portals:EmployeeUrl` | http://localhost:5001 | `employee-portal` |
| `user` | `Portals:ClientUrl` | http://localhost:5090 | *(none shipped — see [Known gaps](#known-gaps))* |

Rules that follow from the code, and that any change must respect:

- **Highest privilege wins, and it short-circuits.** `administrator` beats
  `employee` beats `user`. An account that is both administrator and employee
  with an unset `AdminUrl` stays on the central app — it is never handed the
  lower-privilege portal as a consolation destination.
- **An unset portal URL is legal and means "stay on the central app."** That is
  the intended way to switch a portal off; it is not a misconfiguration.
- **A configured URL must be an absolute http/https URL, and https outside
  Development** ([`PortalUrlPolicy`](src/CentralLogin.Web/Authentication/PortalUrlPolicy.cs)).
  `PortalRoutingOptionsValidator` runs under `ValidateOnStart`, so a bad value
  **aborts startup** instead of misrouting logins. A value that still fails the
  policy at runtime is logged as a warning naming the portal, and the login
  falls back to the local return URL.
- **No matching role at all** → the account stays on the central app.

Where the values come from, per environment:

| Environment | Source |
|---|---|
| Local dev | the `Portals` section of `src/CentralLogin.Web/appsettings.Development.json` |
| Deployed | `Portals__<Name>Url` environment variables. The AppHost forwards only non-empty values (`src/CentralLogin.AppHost/AppHost.cs`); Pulumi derives them from `central-login:domain` — or from explicit `central-login:adminPortalUrl` / `central-login:employeePortalUrl` config — and exports them. |
| `appsettings.json` | placeholders (`https://REPLACE_WITH_..._HOST`) — a template, never a live value |

### Adding a portal for an existing role

This is the common case — a new application for administrators, employees or
client users. **No application code changes are needed**: it is a Keycloak
client plus one configuration value.

1. **Register a confidential OIDC client** for the portal. For local dev add it
   to `keycloak/realm/central-login-realm.json` alongside `admin-portal` (that
   entry is the template); for a deployed realm create it in the admin console
   or through your realm-management pipeline. Required shape:
   - `publicClient: false`, `standardFlowEnabled: true`,
     `implicitFlowEnabled: false`, `directAccessGrantsEnabled: false`
   - `attributes["pkce.code.challenge.method"] = "S256"`
   - `redirectUris`: the **exact** callback, `<portal-base-url>/signin-oidc`
   - `webOrigins`: the portal's base URL
   - `attributes["post.logout.redirect.uris"]`:
     `<portal-base-url>/signout-callback-oidc` (`##`-separated if several)
   - the **realm roles** protocol mapper (copy it from `admin-portal`) so the
     portal also receives `realm_access.roles`
2. **Set the client secret out of band.** Never commit it — a test scans
   `keycloak/` and fails the build if a secret or password reappears. Locally,
   follow the `keycloak/configure-dev.sh` pattern
   (`kcadm.sh update clients/<uuid> -r central-login -s "secret=..."`, with the
   value read from your environment).
3. **Point the role's portal URL at it** — `Portals:AdminUrl`,
   `Portals:EmployeeUrl` or `Portals:ClientUrl`:
   `appsettings.Development.json` for local dev, `Portals__AdminUrl` (etc.) for
   deployed environments, or
   `pulumi config set central-login:adminPortalUrl https://…` for stacks Pulumi
   drives.
4. **Restart the web app** and sign in with an account holding that role (see
   [Test accounts](#test-accounts-local-development)). Add the new port to the
   [Ports](#ports) table if the portal runs locally.

Replacing a portal is the same procedure: register the new client, repoint the
URL, then retire the old client.

### Adding a new kind of portal (a new role → a new destination)

A portal for a role that does not exist yet is a code change, because the
role → portal mapping is explicit by design. Work through all of it — a
half-applied change either aborts startup or silently strands accounts:

| # | File | Change |
|---|---|---|
| 1 | `src/CentralLogin.Application/Authorization/ApplicationRoles.cs` | add the role constant; the string must match the Keycloak realm role exactly |
| 2 | `keycloak/realm/central-login-realm.json` | add the realm role under `roles.realm`, the portal client (previous section), and a password-less local test user holding the role |
| 3 | `src/CentralLogin.Web/Authentication/PortalRoutingOptions.cs` | add the `<Name>Url` property |
| 4 | `src/CentralLogin.Web/Authentication/PortalRouter.cs` | add the branch in `ResolveHomePortalUrl`, positioned deliberately in the privilege order |
| 5 | `src/CentralLogin.Web/Authentication/PortalRoutingOptionsValidator.cs` | add the matching `ValidateUrl(...)` call |
| 6 | `appsettings.json` / `appsettings.Development.json` | the placeholder value, and the local dev URL |
| 7 | `src/CentralLogin.AppHost/AppHost.cs` | add the key to the portal-forwarding array so the value reaches deployed containers |
| 8 | `infrastructure/CentralLogin.Pulumi/` | `StackConfiguration` (`config.Get("<name>PortalUrl")`), `PortalsPlan` + `ResourcePlan.FromConfiguration`, and the provider export / `InfrastructureOutputs` |
| 9 | `tests/` | `PortalRouterTests`, `PortalRoutingOptionsValidatorTests`, `PortalRoutingOptionsStartupValidationTests` |
| 10 | `README.md` | this section, [Ports](#ports), [Test accounts](#test-accounts-local-development) |

If a portal should be reachable by *any* authenticated account rather than by
one role, do **not** invent a role for it: leave routing alone and let the
portal redirect to central login on its own. Portal routing decides only where
an account *lands* after sign-in — it is not an access control mechanism.

### Removing a portal

**Switch it off (no code change, reversible).** Clear the role's portal URL —
remove the key or set it to an empty string in `appsettings.Development.json`,
drop the `Portals__<Name>Url` environment variable, or
`pulumi config rm central-login:adminPortalUrl`. Accounts homed to that role
now stay on the central app after sign-in. Optionally disable — rather than
delete — the Keycloak client, which stops it accepting logins while keeping its
configuration.

**Remove it permanently.** In this order, so no in-flight login is bounced at a
dead client:

1. Clear the portal URL everywhere, as above, and roll that out first.
2. Delete or disable the Keycloak client — in
   `keycloak/realm/central-login-realm.json` for dev, and in each deployed
   realm. The import file only runs on first creation of the realm, so it never
   removes anything from a realm that already exists.
3. Decide what happens to the accounts: reassign them to another role, or
   accept that they now stay on the central app. A role with no portal behind it
   is valid — just make sure it is deliberate.
4. If that *kind* of portal is gone for good, reverse the table above — the
   option property, the `PortalRouter` branch, the validator line, the
   configuration keys, the AppHost entry, the Pulumi config/plan/export, the
   tests and the docs. Removing the role constant is the last step, and only
   once nothing in the realm or in the authorization policies references it.

### Known gaps

- **The client portal (`user` role) has no portal application in this
  repository.** `appsettings.Development.json` sets
  `Portals:ClientUrl=http://localhost:5090`, but the realm import ships no
  client for it and nothing listens on that port — so a `user`-role account
  signing in locally (`central.portal`, for example) is redirected to a URL that
  will not answer. Either clear `Portals:ClientUrl` for local dev, or register
  the client and run the portal before relying on it.
- **Pulumi carries only the admin and employee URLs.** `PortalsPlan` has no
  client-portal member, so `Portals__ClientUrl` is never derived from the
  configured domain for deployed environments; supply it explicitly if that
  portal exists.

## Test accounts (local development)

> **Local development only.** By design the repository commits **no passwords**
> (see [Security](#security)). The realm import creates all of its local users
> **without** a password, and the Keycloak admin password is whatever you supply
> via env / user-secrets. So the passwords below are **conventional local-dev
> values you set yourself** with the commands that follow — they let the next
> developer sign in immediately. Never reuse them in staging/production.

| Account | Username | Password (local dev) | Roles | Where to use it |
|---|---|---|---|---|
| **Keycloak admin** (master realm) | `admin` | `TroqueEstaSenha123!` | — | Admin console: http://localhost:8080/admin |
| **App test user** (`central-login` realm) | `dev-admin` | `DevAdmin123!` | `user`, `administrator` | The web app: http://localhost:5080 |
| **Central portal test user** | `central.portal` | `CentralPortal123!` | `user` | The web app: http://localhost:5080 — but while `Portals:ClientUrl` is set it is routed there after sign-in ([Portals](#portals)) |
| **Employee portal test user** | `employee.central` | `EmployeeCentral123!` | `employee` | EmployeePortal: http://localhost:5001 |
| **Admin portal test user** | `admin.portal` | `AdminPortal123!` | `administrator` | AdminPortal: http://localhost:5100 |

`dev-admin` (display name **Dev Admin**, email `dev-admin@example.local`) holds
both the `user` and `administrator` realm roles, so it can reach the app's
admin-only pages (`/Admin`). `central.portal`, `employee.central` and
`admin.portal` are separate accounts matching the client each is meant to sign
in to, so each portal can be exercised without borrowing `dev-admin`. Which
portal an account actually lands on after sign-in is decided by its realm role —
see [Portals](#portals).

> The three passwords above are new values chosen for this local-dev
> convention — they intentionally **do not** reuse `Central123!`,
> `Employee123!` or `Admin123!`, which were committed by mistake in an earlier
> revision of the realm import and must be treated as burned.

> **Already ran Keycloak before this change?** The realm import only runs
> once, on first creation of the database/volume — so if you started Keycloak
> before this fix, `Central123!`, `Employee123!` and `Admin123!` are still live
> on your existing `central.portal`, `employee.central` and `admin.portal`
> accounts. Re-run `keycloak/configure-dev.sh` (with the new passwords above)
> to overwrite them; the import file itself will not do this for you.

**Setting these passwords.** The Keycloak `admin` password is set when the
container first starts (`KC_BOOTSTRAP_ADMIN_PASSWORD`, see
[Local execution → B](#local-execution) — use `admin` locally). The other
passwords are set **after** the realm import by `keycloak/configure-dev.sh`,
which reads each of them from the environment:

```zsh
export KEYCLOAK_URL=http://localhost:8080
export KEYCLOAK_ADMIN_USERNAME=admin KEYCLOAK_ADMIN_PASSWORD='admin'
export CENTRALLOGIN_WEB_CLIENT_SECRET='<same secret you set for the web app>'
export KEYCLOAK_DEV_ADMIN_PASSWORD='dev-admin'                      # local-dev convention
export KEYCLOAK_CENTRAL_PORTAL_PASSWORD='CentralPortal123!'         # local-dev convention
export KEYCLOAK_EMPLOYEE_CENTRAL_PASSWORD='EmployeeCentral123!'     # local-dev convention
export KEYCLOAK_ADMIN_PORTAL_PASSWORD='AdminPortal123!'             # local-dev convention
./keycloak/configure-dev.sh
```

On **Windows/PowerShell** the same, with `$env:` assignments, e.g.
`$env:KEYCLOAK_DEV_ADMIN_PASSWORD = 'dev-admin'`, then run the script via Git
Bash / WSL. You can also set any of these passwords by hand in the admin
console (**Users → &lt;username&gt; → Credentials → Set password**, turn
*Temporary* off).

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
needed. They also cover the deny-by-default fallback authorization policy
(see [Security](#security)): that every discovered Razor Page declares
authorization intent, that an attribute-less endpoint is rejected, and that
requests matching no endpoint at all get a plain anonymous 404 rather than an
authentication challenge (including a real static file still being served,
and an unmatched path with a file extension such as `/favicon.ico`). The
architecture tests enforce the layering rules.

## Database & migrations

`Database:Provider` selects the EF Core provider (`DatabaseOptionsValidator`
enforces this is configured correctly — see
[Configuration & secrets](#configuration--secrets)):

- **`Sqlite`** (default, local development). Lives in a configurable
  persistent directory (`Database:DataDirectory`, default `./data`), with WAL
  journaling, `synchronous=NORMAL`, a busy timeout and foreign keys enabled,
  and a unique index on `KeycloakSubject`. The directory and database file are
  created automatically — there is no `.db` file checked into git, and no
  manual setup step: the first `dotnet run` (or `aspire run`) creates
  `data/centrallogin.db` and applies all pending EF Core migrations at
  startup. SQLite allows only one writer at a time, so it is unsuitable for
  more than one replica — see
  [Why SQLite locally, PostgreSQL in production](#why-sqlite-locally-postgresql-in-production).
- **`Postgres`** (deployed environments). Requires `Database:ConnectionString`
  (never committed — a Container App secret in production, see
  [Pulumi infrastructure](#pulumi-infrastructure)). Every replica applies
  migrations against the same server at startup; EF Core 10 takes a migration
  lock, so concurrent replicas serialize rather than race.

The two providers keep **separate migration sets in separate assemblies**,
because EF Core scans a whole assembly for migrations: SQLite's migrations
stay in `CentralLogin.Infrastructure` (the default migrations assembly),
PostgreSQL's live in `CentralLogin.Infrastructure.Postgres` (set explicitly via
`MigrationsAssembly` in `DependencyInjection.cs`). There is only **one** shared
model (`CentralLoginDbContext`), so **every** model change needs a migration
added to **both** assemblies — adding one and forgetting the other lets
PostgreSQL silently drift behind SQLite (or vice versa) until it fails at
runtime. `CentralLoginDbContextPendingModelChangesTests` guards against this:
it fails the build the moment either provider has a pending model change.

```zsh
# SQLite:
dotnet tool run dotnet-ef migrations add <Name> \
  --project src/CentralLogin.Infrastructure \
  --output-dir Persistence/Migrations

# PostgreSQL:
dotnet tool run dotnet-ef migrations add <Name> \
  --project src/CentralLogin.Infrastructure.Postgres \
  --output-dir Persistence/Migrations

# Apply migrations manually (normally automatic at startup):
dotnet tool run dotnet-ef database update --project src/CentralLogin.Infrastructure           # SQLite
dotnet tool run dotnet-ef database update --project src/CentralLogin.Infrastructure.Postgres   # PostgreSQL
```

`CentralLoginPostgresDbContextFactory` targets a design-time placeholder
connection string (no live database needed to add/scaffold a migration), so
`dotnet-ef database update --project ...Infrastructure.Postgres` against a
real server also needs an explicit `--connection "<your connection string>"`
— otherwise it silently targets the placeholder.

## Data Protection key ring

The auth cookie (and, via `SaveTokens = true`, the OIDC tokens saved inside it)
is encrypted with the ASP.NET Core Data Protection key ring. Bound from the
`DataProtection` section (`DataProtectionKeyRingOptions`, validated at startup):

```json
"DataProtection": {
  "ApplicationName": "CentralLogin",
  "KeyRingPath": "/mnt/centrallogin-keys"
}
```

- **`ApplicationName`** is embedded in every protected payload and must stay the
  same across every deployment/instance — changing it invalidates every
  existing cookie even if the key ring itself is preserved. Defaults to
  `CentralLogin`; only override it if you must (e.g. running two differently
  named instances that must NOT share cookies).
- **`KeyRingPath`** persists the key ring to that directory
  (`PersistKeysToFileSystem`). Without it, ASP.NET Core's default is to persist
  the key ring **per-user** under the OS profile directory
  (`%LOCALAPPDATA%\ASP.NET\DataProtection-Keys`, DPAPI-encrypted, on Windows;
  `~/.aspnet/DataProtection-Keys`, unencrypted, on Linux/macOS) — that default
  is fine for a developer's own machine, but neither outcome in a container is:
  with no profile directory the framework silently falls back to **ephemeral,
  in-memory keys**, and where `HOME`/`LOCALAPPDATA` *is* set it persists into
  the container's **writable layer** — either way the keys are regenerated on
  the next start or redeploy, exactly the failure this option exists to
  prevent. **Required outside Development** — startup fails loudly if it is
  unset or not an absolute path, naming the offending
  key — because without it every restart, redeploy or second instance
  regenerates the keys and silently signs every user out. It must point at a
  **durable, shared volume** mounted into every instance (e.g. a Kubernetes
  `PersistentVolumeClaim` with `ReadWriteMany`, an NFS/EFS mount, or an Azure
  File Share) — a container-local or per-instance path defeats the point, and
  a relative path would resolve against the working directory (the writable
  image layer), which is why it must be absolute. Left unset in Development,
  where the framework's per-user default is fine. Set it via
  `DataProtection__KeyRingPath` (environment variable) if you would rather not
  add it to `appsettings.json`, same as `Keycloak:ClientSecret`.
- The key ring directory holds unencrypted key material once populated on
  Linux/macOS (on Windows with a user profile the files are still
  DPAPI-encrypted). The app creates it owner-only (`0700`) on Linux/macOS so
  no other local user/container on the same host can read it (a no-op if the
  volume already exists with its own permissions); Windows relies on the
  mounted volume's own ACLs. Treat it like any other secret store: prefer an
  already-encrypted-at-rest volume, and for stronger at-rest protection
  `IDataProtectionBuilder` also supports `ProtectKeysWithCertificate(...)` if
  the hosting platform provides a suitable certificate/KMS.
- **Verifying persistence survives a restart**: `dotnet test` covers this
  (`DataProtectionKeyRingPersistenceTests` — two independently composed hosts
  pointed at the same `KeyRingPath` decrypt each other's payload, proving a
  restart does not lose the key ring). To check by hand: start the app with a
  `KeyRingPath` set, sign in, stop the app, start it again, and confirm the
  existing session cookie is still valid rather than redirecting to sign-in.

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
container registry, DNS, TLS, secret management, logging, monitoring and
backups. Sensitive config is marked secret; only non-sensitive outputs are
exported. See `infrastructure/CentralLogin.Pulumi/README.md` for
`pulumi preview` / `up` / `refresh` / `destroy` and provider extension points.
No resources are created until a provider is selected.

On Azure, the `web` Container App is configured against a PostgreSQL Flexible
Server provisioned for Keycloak — its own `centrallogin` database on that
server, reached via a Container App secret — never a plaintext connection
string, which is what makes `MinInstances = 2` in production safe: every
replica shares one database instead of each maintaining its own
container-local SQLite file. Keycloak's own database is not yet wired up (no
`keycloak` database resource, no `KC_DB*` env vars on the Keycloak Container
App) — the server exists for that purpose but only the application's database
is provisioned today. The Storage Account that an earlier revision provisioned
for "persistent storage" but never attached to anything has been removed.

The web app authenticates to PostgreSQL as the server administrator
(`kcadmin`) — the same account reserved for Keycloak — because ARM cannot
provision a scoped role for Flexible Server. That is a documented trade-off,
not an oversight: a dedicated least-privilege role for the `web` app is the
follow-up, and until then its credentials can read/drop every database on the
server (see the comment at the connection-string construction in
`AzureCloudProvider.cs`).

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

## Why SQLite locally, PostgreSQL in production

The application database stores only application-specific data (the local
`ApplicationUser` projection keyed by the Keycloak subject) — nothing here
holds identity secrets. The data layer sits behind repository interfaces in
the Application layer, so `Database:Provider` switches the concrete EF Core
provider in Infrastructure without touching domain or web code (see
[Database & migrations](#database--migrations)).

**SQLite** (`Database:Provider=Sqlite`, the local-development default) is
zero-ops, file-based and transactional — ideal for a single instance, and it
is what every unit/integration test and the Aspire AppHost use. It allows only
one writer at a time, and its WAL mode is not safe over a network file share
(byte-range locking is not reliably forwarded over SMB), so it cannot be
shared across replicas.

**PostgreSQL** (`Database:Provider=Postgres`) is what deployed environments
use instead, because the production plan runs the `web` Container App with
`MinReplicas = 2`: every replica must share one writable store rather than
each maintaining its own container-local SQLite file, and writes are not
rare here — `UserProvisioningService.RecordLogin` persists on **every**
successful sign-in, not just the first one. The Pulumi Azure provider points
the app at a PostgreSQL Flexible Server provisioned for Keycloak (see
[Pulumi infrastructure](#pulumi-infrastructure)).

## Security

- **No secrets in source control.** Credentials, password hashes, refresh
  tokens, client secrets and Keycloak-internal data are never stored in the
  application database (SQLite or PostgreSQL) or committed. Secrets flow via
  user-secrets / Aspire parameters (dev) and environment/secret manager
  (staging/prod). `appsettings*.json` hold no secrets.
- **Token handling.** Standard ASP.NET Core OIDC middleware validates issuer,
  audience, lifetime and signing keys, and handles state/nonce/PKCE. We never
  parse or validate JWT signatures manually. Tokens, authorization codes, cookies
  and secrets are never logged (the provisioning log emits only a subject hash).
- **Cookies.** HttpOnly, `SameSite=Lax`, Secure outside development (`__Host-`
  prefix in non-dev). HTTPS + HSTS enforced outside development.
- **Data Protection key ring.** Persisted to a durable, shared volume outside
  Development with a stable application name — see [Data Protection key
  ring](#data-protection-key-ring) — so restarts, redeploys and additional
  instances do not silently invalidate every session cookie.
- **CSRF.** Razor Pages antiforgery protects state-changing (POST) requests,
  including logout.
- **Deny-by-default authorization.** The fallback authorization policy
  (`AddApplicationAuthorization`) requires an authenticated user on any
  endpoint that carries no authorization metadata of its own, so a newly
  added page is unreachable until someone deliberately opts it out with
  `[AllowAnonymous]` — nothing is implicitly public.
  `EndpointAuthorizationCoverageTests` fails the build the moment a page has
  neither an authorization requirement nor that opt-out. The fallback policy
  is also evaluated for requests that match no endpoint at all; `Program.cs`
  maps an explicit, anonymous catch-all so unmatched paths (a typo'd URL, a
  missing static asset, `/favicon.ico`) still 404 for everyone instead of
  triggering an OIDC challenge.
- **Headers & CSP.** `SecurityHeadersMiddleware` sets a Razor-appropriate CSP
  (no inline/eval scripts), `X-Content-Type-Options`, `X-Frame-Options: DENY`,
  `Referrer-Policy`, `Cross-Origin-Opener-Policy` and `Permissions-Policy`.
- **Rate limiting** is applied to authentication-initiating endpoints.
- **Dependency scanning** runs in CI (`dotnet list package --vulnerable` via the
  Cake `VulnerabilityCheck` task).
- **Keycloak admin console** must not be exposed publicly without restrictions;
  production uses exact redirect URIs and HTTPS.
- **Database credential scope (known deferral).** On Azure the `web` app
  connects to PostgreSQL as the Flexible Server administrator (`kcadmin`) —
  the same account reserved for Keycloak — because ARM has no way to create a
  scoped PostgreSQL role for Flexible Server. Its credentials can therefore
  read/drop every database on the server. This is a documented trade-off, not
  an oversight; a dedicated least-privilege role for the `web` app is the
  follow-up (see [Pulumi infrastructure](#pulumi-infrastructure)).
- **No production admins are created at app startup.** The `dev-admin` user
  exists only in the development realm import, without a committed password.
- **Secret rotation.** Rotate the client secret in the Keycloak admin console
  (Clients → central-login-web → Credentials → Regenerate) or via `kcadm.sh`, then
  update `Keycloak:ClientSecret` in the app's secret store. Rotate on a schedule
  and after any suspected exposure. See `keycloak/README.md`.
- **Backup & restore.** Local SQLite: back up the database file and its
  `-wal`/`-shm` siblings (or use `sqlite3 .backup`) from the persistent data
  directory; restore by replacing the files while the app is stopped. Deployed
  PostgreSQL (the application's `centrallogin` database, on the Flexible
  Server provisioned for Keycloak): use managed PostgreSQL backups / `pg_dump`;
  the Pulumi `BackupPlan` prepares retention settings per environment. Owning
  team: whoever operates the deployment is responsible for scheduling and
  testing restores.

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
