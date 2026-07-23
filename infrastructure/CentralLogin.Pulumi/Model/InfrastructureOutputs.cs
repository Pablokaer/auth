using Pulumi;

namespace CentralLogin.Pulumi.Model;

/// <summary>
/// Non-sensitive stack outputs. ONLY non-secret values belong here — secrets
/// (passwords, client secrets) must never be exported.
/// </summary>
public sealed class InfrastructureOutputs
{
    public Output<string>? ApplicationUrl { get; init; }
    public Output<string>? KeycloakUrl { get; init; }
    public string? Notes { get; init; }

    public IDictionary<string, object?> ToDictionary()
    {
        var outputs = new Dictionary<string, object?>();

        if (ApplicationUrl is not null)
        {
            outputs["applicationUrl"] = ApplicationUrl;
        }

        if (KeycloakUrl is not null)
        {
            outputs["keycloakUrl"] = KeycloakUrl;
        }

        if (!string.IsNullOrEmpty(Notes))
        {
            outputs["notes"] = Notes;
        }

        return outputs;
    }
}
