namespace CentralLogin.Domain.Users;

/// <summary>
/// Single source of truth for the maximum lengths of <see cref="ApplicationUser"/>
/// fields. Referenced by both the Application layer (validation) and the
/// Infrastructure layer (EF Core column sizing) so the two cannot drift apart.
/// </summary>
public static class ApplicationUserLimits
{
    /// <summary>Max length of the Keycloak <c>sub</c> claim (<see cref="KeycloakSubject"/>).</summary>
    public const int KeycloakSubjectMaxLength = 255;

    /// <summary>Max length of <see cref="ApplicationUser.DisplayName"/>.</summary>
    public const int DisplayNameMaxLength = 256;

    /// <summary>Max length of <see cref="ApplicationUser.EmailSnapshot"/>.</summary>
    public const int EmailSnapshotMaxLength = 320;
}
