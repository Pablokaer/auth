namespace CentralLogin.Application.Users;

/// <summary>
/// Input to the user-provisioning use case, assembled from the validated OIDC
/// claims after successful authentication.
/// </summary>
/// <param name="Subject">Keycloak <c>sub</c> claim — the permanent identity key.</param>
/// <param name="DisplayName">Optional friendly name (e.g. from <c>name</c>).</param>
/// <param name="Email">Optional email snapshot (never used as the identity key).</param>
public sealed record ProvisionUserCommand(string Subject, string? DisplayName, string? Email);
