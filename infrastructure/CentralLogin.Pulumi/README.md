# CentralLogin infrastructure (Pulumi, C#)

Provider-neutral infrastructure as code. The program turns a
[`ResourcePlan`](Model/ResourcePlan.cs) into cloud resources via an
[`ICloudProvider`](Providers/ICloudProvider.cs). Until a provider is chosen the
default [`NotConfiguredCloudProvider`](Providers/NotConfiguredCloudProvider.cs)
creates **nothing**, so the scaffold is safe to preview and apply.

## Stacks

Separate stacks per environment: `dev`, `staging`, `prod`. Create them once:

```zsh
cd infrastructure/CentralLogin.Pulumi
pulumi stack init dev
pulumi stack init staging
pulumi stack init prod
```

## Configuration and secrets

Non-secret config lives in `Pulumi.<stack>.yaml`. **Sensitive values must be set
as secrets** (encrypted by Pulumi, never committed in plaintext):

```zsh
pulumi config set central-login:cloudProvider azure          # none|azure|aws|gcp|kubernetes
pulumi config set central-login:domain auth.example.com
pulumi config set central-login:adminPortalUrl    https://admin.example.com     # optional
pulumi config set central-login:employeePortalUrl https://employee.example.com # optional
pulumi config set --secret central-login:keycloakDbPassword  <value>
pulumi config set --secret central-login:webClientSecret     <value>
```

`adminPortalUrl`/`employeePortalUrl` feed the Web app's `Portals__AdminUrl` /
`Portals__EmployeeUrl` env vars. If left unset but `domain` is configured, they
default to `https://admin.<domain>` and `https://employee.<domain>`; if neither
is available the env var is simply not emitted (never a placeholder value).
Note that `domain` is itself typically a subdomain (`auth.example.com`), so the
derived default nests under it (`admin.auth.example.com`). Set the two portal
keys explicitly whenever the portals live elsewhere.

Only non-sensitive outputs are exported (e.g. `applicationUrl`, `keycloakUrl`).
Secrets never appear in outputs or in plain (non-secret-ref) container env vars
— see the Azure section below for how they reach the Container Apps.

## What the plan prepares

The `ResourcePlan` models, provider-agnostically: application hosting, Keycloak
hosting, PostgreSQL for Keycloak, persistent storage, container registry, DNS,
TLS certificates, secret management, logging, monitoring and backups. Each cloud
provider implementation maps these to concrete resources.

## Azure (selected provider)

`AzureCloudProvider` (`Providers/Azure/`) is implemented with
`Pulumi.AzureNative` `3.23.0` and provisions: a resource group, Log Analytics
(logging + monitoring), Azure Container Registry, a Storage Account (persistent
storage), Key Vault (secret management, RBAC), a user-assigned managed
identity, a PostgreSQL Flexible Server for Keycloak (with the `keycloak`
database, backup retention, and an "allow Azure services" firewall rule), and
a Container Apps environment hosting the Web app and Keycloak — both fully
wired with the environment variables and secrets they read at runtime:

- **Keycloak** gets `KC_DB`/`KC_DB_URL`/`KC_DB_USERNAME` and a `KC_DB_PASSWORD`
  secret reference pointing at the Flexible Server, plus the ingress-related
  settings (`KC_HTTP_ENABLED`, `KC_PROXY_HEADERS`, `KC_HOSTNAME_STRICT=false`,
  health/metrics) needed to run behind Container Apps. `KC_HOSTNAME` is
  deliberately **not** set: Keycloak's own ingress FQDN is not knowable before
  the app exists, so it resolves its hostname from the request and therefore
  advertises the same issuer that `Keycloak__Authority` is built from. Set
  `KC_HOSTNAME` only once a custom domain is bound to the Keycloak app, and
  update `Keycloak__Authority` in lockstep.
- **Web** gets `Keycloak__Authority`, `Keycloak__ClientId`, a `Keycloak__ClientSecret`
  secret reference, and `Portals__AdminUrl`/`Portals__EmployeeUrl`.
- Both `keycloakDbPassword` and `webClientSecret` are stored as Key Vault
  secrets; the Container Apps read them via `Configuration.Secrets`
  (`KeyVaultUrl` + the shared managed identity) and reference them from
  containers with `SecretRef` — never as literal env values.

**Database connectivity tradeoff:** the Flexible Server firewall rule uses the
documented `0.0.0.0`/`0.0.0.0` sentinel, which means "allow other Azure
services", not "allow the whole internet". A private endpoint (VNet
integration) is the hardening step over this, left out here because it needs
its own infrastructure subnet.

DNS zone + custom-domain/TLS binding is left as a marked extension point
because it depends on your verified domain — this is the one piece the stack
does not finish end to end.

Provide your own account values (nothing is invented):

```zsh
cd infrastructure/CentralLogin.Pulumi
pulumi login
pulumi stack select dev
az login                                            # Azure credentials for the provider
pulumi config set central-login:azureLocation westeurope
pulumi config set central-login:azureTenantId <tenant-id>
pulumi config set azure-native:subscriptionId <subscription-id>
pulumi config set central-login:domain auth.example.com
pulumi config set --secret central-login:keycloakDbPassword "$(openssl rand -base64 24)"
pulumi config set --secret central-login:webClientSecret   "$(openssl rand -base64 32)"
pulumi preview
```

## Selecting a different provider (extension point)

1. Add the provider package to `CentralLogin.Pulumi.csproj`
   (e.g. `Pulumi.AzureNative`, `Pulumi.Aws`, `Pulumi.Gcp`, `Pulumi.Kubernetes`).
2. Implement `ICloudProvider` in a new class (e.g. `AzureCloudProvider`).
3. Register it in [`CloudProviderFactory`](Providers/CloudProviderFactory.cs).
4. Set `central-login:cloudProvider` for each stack.

No cloud account identifiers are invented anywhere; supply your own via config.

## Common commands

```zsh
pulumi preview --stack dev      # show planned changes (no apply)
pulumi up --stack dev           # apply changes
pulumi refresh --stack dev      # reconcile state with real resources
pulumi destroy --stack dev      # tear down (never run casually on prod)
```

## Production protection

Enable delete protection and require manual approval for production:

```zsh
pulumi stack select prod
pulumi config set central-login:cloudProvider <provider>
# Protect critical resources in code with `CustomResourceOptions { Protect = true }`.
```

Production deploys run only through the GitHub deployment workflow, which uses a
protected `production` environment requiring a manual approval. The Cake
`PulumiDeploy` task additionally refuses to deploy to Production without an
explicit `--environment=Production --confirm-production=true`.
