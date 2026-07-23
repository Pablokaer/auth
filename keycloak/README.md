# Keycloak

Central identity provider for CentralLogin. Runs from the official image,
pinned to `quay.io/keycloak/keycloak:26.5.6`.

## Development vs production

| | Development | Production |
|---|---|---|
| Start command | `start-dev` (via Aspire AppHost) | `start` (optimized build) |
| Database | Dev-mode local store (simplified) | **PostgreSQL** (`postgres:17.6`) |
| TLS | HTTP on localhost | HTTPS required, behind a proxy |
| Realm | Imported from `realm/central-login-realm.json` | Managed/promoted deliberately |
| Admin console | Local only | Restricted — never public without controls |

The AppHost runs Keycloak with `start-dev --import-realm`. **Do not use
`start-dev` in production** — use `start` with `keycloak.conf` (see
`keycloak.conf.example`) and the production variables: hostname, proxy headers,
HTTPS, health endpoint, metrics, database connection and bootstrap admin.

## The realm import contains no secrets

`realm/central-login-realm.json` defines:

- realm `central-login`
- confidential client `central-login-web` (Authorization Code + PKCE S256),
  with **exact** dev redirect URIs and post-logout redirect URIs
- realm roles `user` and `administrator`
- a `dev-admin` user with those roles but **no password**

It deliberately does **not** contain the client secret or any password.

## First-time local setup (secret + dev-admin password)

After the AppHost starts Keycloak and imports the realm, run once:

```zsh
export KEYCLOAK_URL=http://localhost:8080
export KEYCLOAK_ADMIN_USERNAME="$KEYCLOAK_ADMIN_USERNAME"
export KEYCLOAK_ADMIN_PASSWORD="$KEYCLOAK_ADMIN_PASSWORD"
export CENTRALLOGIN_WEB_CLIENT_SECRET="$(openssl rand -base64 32)"   # or your chosen value
export KEYCLOAK_DEV_ADMIN_PASSWORD="$(openssl rand -base64 24)"
./keycloak/configure-dev.sh
```

Then store the same `CENTRALLOGIN_WEB_CLIENT_SECRET` for the web app via
user-secrets so the app and Keycloak agree:

```zsh
dotnet user-secrets --project src/CentralLogin.Web set "Keycloak:ClientSecret" "$CENTRALLOGIN_WEB_CLIENT_SECRET"
```

## Creating or rotating the client secret

Admin console: **Clients → central-login-web → Credentials → Regenerate**, then
update the app's `Keycloak:ClientSecret` (user-secret in dev; environment/secret
manager in staging/production). Or via `kcadm.sh`:

```zsh
kcadm.sh update clients/<client-uuid> -r central-login -s "secret=<new-secret>"
```

Rotate on a schedule and whenever a secret may have been exposed. See the main
README's Security section for the full rotation checklist.
