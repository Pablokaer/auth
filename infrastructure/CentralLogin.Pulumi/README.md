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
pulumi config set --secret central-login:keycloakDbPassword  <value>
pulumi config set --secret central-login:webClientSecret     <value>
```

Only non-sensitive outputs are exported (e.g. `applicationUrl`, `keycloakUrl`).

## What the plan prepares

The `ResourcePlan` models, provider-agnostically: application hosting, Keycloak
hosting, PostgreSQL for Keycloak, persistent storage, container registry, DNS,
TLS certificates, secret management, logging, monitoring and backups. Each cloud
provider implementation maps these to concrete resources.

## Azure (selected provider)

`AzureCloudProvider` (`Providers/Azure/`) is implemented with
`Pulumi.AzureNative` `3.23.0` and provisions: a resource group, Log Analytics
(logging + monitoring), Azure Container Registry, a Storage Account (persistent
storage), Key Vault (secret management, RBAC), a PostgreSQL Flexible Server for
Keycloak (with backup retention), and a Container Apps environment hosting the
Web app and Keycloak. DNS zone + custom-domain/TLS binding is left as a marked
extension point because it depends on your verified domain.

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
