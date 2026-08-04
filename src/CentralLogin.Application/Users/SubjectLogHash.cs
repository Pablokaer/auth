using System.Security.Cryptography;
using System.Text;

namespace CentralLogin.Application.Users;

/// <summary>
/// Produces a short, stable, non-reversible tag for correlating log lines about
/// a Keycloak subject without ever emitting the raw identifier itself. Shared
/// between the Application layer (successful provisioning) and the Web layer
/// (provisioning failures logged at the OIDC boundary) so both use the same
/// correlation tag for the same subject.
/// </summary>
public static class SubjectLogHash
{
    public static string Compute(string subject)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(subject), hash);
        return Convert.ToHexString(hash)[..12];
    }
}
