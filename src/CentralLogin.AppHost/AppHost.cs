using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

// =====================================================================
// CentralLogin — .NET Aspire AppHost (local development orchestrator)
// =====================================================================
// Brings up, with a single run:
//   * PostgreSQL          — the production database for Keycloak (persistent)
//   * Keycloak            — official pinned image, imports the dev realm
//   * CentralLogin.Web    — waits for Keycloak, wired to the realm + client secret
//
// SECRETS ARE PARAMETERS, never hardcoded. Supply values via user-secrets:
//   dotnet user-secrets --project src/CentralLogin.AppHost set Parameters:keycloak-admin-username admin
//   dotnet user-secrets --project src/CentralLogin.AppHost set Parameters:keycloak-admin-password  <strong>
//   dotnet user-secrets --project src/CentralLogin.AppHost set Parameters:keycloak-db-password     <strong>
//   dotnet user-secrets --project src/CentralLogin.AppHost set Parameters:central-login-web-client-secret <strong>
// ---------------------------------------------------------------------

const string KeycloakImage = "quay.io/keycloak/keycloak";
const string KeycloakTag = "26.5.6";   // pinned official image
const string KeycloakDatabaseName = "keycloak";

var builder = DistributedApplication.CreateBuilder(args);

// --- Secret parameters (resolved from configuration / user-secrets) ----------
var keycloakAdminUser = builder.AddParameter("keycloak-admin-username");
var keycloakAdminPassword = builder.AddParameter("keycloak-admin-password", secret: true);
var keycloakDbPassword = builder.AddParameter("keycloak-db-password", secret: true);
var webClientSecret = builder.AddParameter("central-login-web-client-secret", secret: true);

// --- PostgreSQL (Keycloak's production database; persistent locally) ----------
var postgres = builder.AddPostgres("keycloak-postgres", password: keycloakDbPassword)
    .WithEnvironment("POSTGRES_DB", KeycloakDatabaseName) // ensure the DB exists on first init
    .WithDataVolume("centrallogin-keycloak-pgdata")
    .WithLifetime(ContainerLifetime.Persistent);

var postgresEndpoint = postgres.GetEndpoint("tcp");

// --- Keycloak (official container image, pinned) ------------------------------
var keycloak = builder.AddContainer("keycloak", KeycloakImage, KeycloakTag)
    // Bootstrap admin for the Keycloak master realm console (not an app user).
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", keycloakAdminUser)
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", keycloakAdminPassword)
    .WithEnvironment("KC_HEALTH_ENABLED", "true")
    .WithEnvironment("KC_METRICS_ENABLED", "true")
    // Bind Keycloak to PostgreSQL (mirrors the production topology).
    .WithEnvironment("KC_DB", "postgres")
    .WithEnvironment("KC_DB_USERNAME", "postgres")
    .WithEnvironment("KC_DB_PASSWORD", keycloakDbPassword)
    .WithEnvironment(context =>
    {
        context.EnvironmentVariables["KC_DB_URL"] = ReferenceExpression.Create(
            $"jdbc:postgresql://{postgresEndpoint.Property(EndpointProperty.Host)}:{postgresEndpoint.Property(EndpointProperty.Port)}/{KeycloakDatabaseName}");
    })
    // Import the development realm (no secrets in the import file).
    .WithBindMount("../../keycloak/realm", "/opt/keycloak/data/import", isReadOnly: true)
    // Stable ports so browser cookies / OIDC authority remain valid across restarts.
    .WithHttpEndpoint(port: 8080, targetPort: 8080, name: "http")
    .WithHttpEndpoint(port: 9000, targetPort: 9000, name: "management")
    // DEV ONLY: start-dev. Production must use the optimized `start` command.
    .WithArgs("start-dev", "--import-realm")
    .WithHttpHealthCheck(path: "/health/ready", endpointName: "management")
    .WaitFor(postgres);

var keycloakHttp = keycloak.GetEndpoint("http");

// --- Web application ----------------------------------------------------------
var web = builder.AddProject<Projects.CentralLogin_Web>("web")
    .WithExternalHttpEndpoints()
    .WaitFor(keycloak)
    .WithEnvironment("Keycloak__ClientSecret", webClientSecret)
    .WithEnvironment("Keycloak__RequireHttpsMetadata", "false")
    .WithEnvironment(context =>
    {
        context.EnvironmentVariables["Keycloak__Authority"] =
            ReferenceExpression.Create($"{keycloakHttp.Property(EndpointProperty.Url)}/realms/central-login");
    });

builder.Build().Run();
