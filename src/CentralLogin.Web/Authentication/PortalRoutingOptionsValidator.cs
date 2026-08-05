using Microsoft.Extensions.Options;

namespace CentralLogin.Web.Authentication;

/// <summary>
/// Fails fast at startup (via <c>ValidateOnStart</c>) when a configured
/// portal URL cannot be trusted as a post-login redirect target: it must be
/// an absolute http/https URL, and https is required outside Development
/// (see <see cref="PortalUrlPolicy"/>). Both URLs are optional — an unset
/// portal simply means accounts homed there stay on this central app — so
/// null/empty values are not validation failures.
/// </summary>
public sealed class PortalRoutingOptionsValidator : IValidateOptions<PortalRoutingOptions>
{
    private readonly bool _requireHttps;

    public PortalRoutingOptionsValidator(IHostEnvironment environment)
        => _requireHttps = !environment.IsDevelopment();

    public ValidateOptionsResult Validate(string? name, PortalRoutingOptions options)
    {
        var failures = new List<string>();

        ValidateUrl(nameof(PortalRoutingOptions.AdminUrl), options.AdminUrl, failures);
        ValidateUrl(nameof(PortalRoutingOptions.EmployeeUrl), options.EmployeeUrl, failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private void ValidateUrl(string propertyName, string? value, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value) || PortalUrlPolicy.TryParse(value, _requireHttps, out _))
        {
            return;
        }

        // Name the actual reason: a value that would be a valid absolute
        // http/https URL if only https were not required gets a message
        // about https being required, not the generic "must be an absolute
        // http/https URL" (which would be misleading for e.g. a plain
        // http://... URL outside Development).
        var message = _requireHttps && PortalUrlPolicy.TryParse(value, requireHttps: false, out _)
            ? $"{PortalRoutingOptions.SectionName}:{propertyName} '{value}' uses http, but https is required outside Development."
            : $"{PortalRoutingOptions.SectionName}:{propertyName} '{value}' must be an absolute http/https URL.";

        failures.Add(message);
    }
}
