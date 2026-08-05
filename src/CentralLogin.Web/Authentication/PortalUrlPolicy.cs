namespace CentralLogin.Web.Authentication;

/// <summary>
/// The rule a portal redirect URL must satisfy before it is trusted as the
/// post-login <c>ReturnUri</c> (see <see cref="PortalRouter"/>) or accepted
/// into <see cref="PortalRoutingOptions"/> at startup.
///
/// The portal URLs come from configuration, not user input, so this is
/// defence in depth rather than a fix for a live open redirect: a
/// misconfigured value (a relative path, a non-http(s) scheme, or plain
/// http in a non-Development environment) should never end up as a
/// redirect target.
/// </summary>
public static class PortalUrlPolicy
{
    /// <summary>
    /// Parses <paramref name="candidate"/> and returns <c>true</c> only when it
    /// is an absolute URI with scheme <c>http</c> or <c>https</c> — and, when
    /// <paramref name="requireHttps"/> is set, specifically <c>https</c>.
    /// An empty/whitespace candidate is <em>not</em> valid by this policy;
    /// callers that treat "unconfigured" as acceptable must check for that
    /// separately (an unset portal legitimately means "stay on central").
    ///
    /// Callers must use the returned <paramref name="uri"/> — never the raw
    /// <paramref name="candidate"/> — as the actual redirect target.
    /// <see cref="Uri.TryCreate(string?, UriKind, out Uri?)"/> tolerates
    /// surrounding whitespace/control characters and backslash-as-separator
    /// tricks (e.g. <c>"http:/\evil.com"</c> parses to host <c>evil.com</c>);
    /// <see cref="Uri.AbsoluteUri"/> is the canonical, whitespace-free form of
    /// whatever host/scheme was actually parsed, so using it closes that gap
    /// instead of forwarding the raw string verbatim.
    /// </summary>
    public static bool TryParse(string? candidate, bool requireHttps, out Uri? uri)
    {
        uri = null;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (requireHttps && parsed.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        uri = parsed;
        return true;
    }
}
