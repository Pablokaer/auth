using System.Text.RegularExpressions;
using Xunit;

namespace CentralLogin.IntegrationTests.Security;

/// <summary>
/// Guards the promise made in <c>keycloak/README.md</c> and
/// <c>src/CentralLogin.AppHost/AppHost.cs</c> that the Keycloak realm import
/// (bind-mounted into the container and loaded at startup) contains no live
/// credentials. Scans every file under <c>keycloak/</c> so a committed
/// password or secret value fails the build instead of silently regressing.
/// </summary>
public sealed class KeycloakRealmSecretsTests
{
    // Catches a Keycloak credential block regardless of JSON formatting/spacing
    // (e.g. the compact "type": "password" as well as the loosely-spaced
    // "type":  "password" produced by some tooling).
    private static readonly Regex PasswordCredentialType =
        new(@"""type""\s*:\s*""password""", RegexOptions.IgnoreCase);

    // Catches a "secret"/"clientSecret" field carrying an actual value, as
    // opposed to the field simply being absent (which is how this realm
    // import documents that it holds no client secret).
    private static readonly Regex SecretField =
        new(@"""(?:secret|clientSecret)""\s*:\s*""(?<value>[^""]*)""", RegexOptions.IgnoreCase);

    // Catches any other bare "password"/"bindCredential" JSON field carrying a
    // value — not just the user "credentials" array. Keycloak realm exports can
    // carry these in several other places (e.g. smtpServer.password, or an LDAP
    // federation provider's bindCredential), and none of them belong in this
    // secret-free import file either.
    private static readonly Regex OtherCredentialField =
        new(@"""(?:password|bindCredential)""\s*:\s*""(?<value>[^""]*)""", RegexOptions.IgnoreCase);

    // Catches a literal (non-variable) password hardcoded into configure-dev.sh
    // in place of the documented `--new-password "${KEYCLOAK_*_PASSWORD}"`.
    private static readonly Regex ShellNewPasswordArgument =
        new(@"--new-password\s+""(?<value>[^""]*)""", RegexOptions.IgnoreCase);

    // Catches a literal (non-variable) client secret hardcoded into
    // configure-dev.sh in place of `"secret=${CENTRALLOGIN_WEB_CLIENT_SECRET}"`.
    private static readonly Regex ShellSecretAssignment =
        new(@"""secret=(?<value>[^""]*)""", RegexOptions.IgnoreCase);

    // Values that are schema placeholders/examples, not real committed secrets.
    private static readonly string[] PlaceholderValues =
    {
        string.Empty,
        "REPLACE_ME",
        "CHANGE_ME",
        "changeme",
    };

    // A value that is entirely one or more shell variable expansions
    // (e.g. "${KEYCLOAK_DEV_ADMIN_PASSWORD}") is a reference, not a literal
    // secret — configure-dev.sh is expected to look exactly like this.
    private static readonly Regex ShellVariableReferenceOnly =
        new(@"^(?:\$\{[A-Za-z_][A-Za-z0-9_]*\})+$");

    [Fact]
    public void Keycloak_directory_contains_no_committed_credential_material()
    {
        var keycloakDirectory = FindKeycloakDirectory();
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(keycloakDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(keycloakDirectory, file);
            var text = File.ReadAllText(file);

            if (PasswordCredentialType.IsMatch(text))
            {
                violations.Add($"{relativePath}: contains a \"type\": \"password\" credential block");
            }

            CollectViolations(SecretField, text, relativePath, violations);
            CollectViolations(OtherCredentialField, text, relativePath, violations);
            CollectViolations(ShellNewPasswordArgument, text, relativePath, violations);
            CollectViolations(ShellSecretAssignment, text, relativePath, violations);
        }

        Assert.True(
            violations.Count == 0,
            "Found committed credential material under keycloak/. Passwords/secrets must be supplied "
                + "via environment variables and keycloak/configure-dev.sh (see keycloak/README.md), never "
                + "committed to the realm import:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static void CollectViolations(Regex pattern, string text, string relativePath, List<string> violations)
    {
        foreach (Match match in pattern.Matches(text))
        {
            var value = match.Groups["value"].Value;
            if (!IsPlaceholder(value))
            {
                violations.Add($"{relativePath}: contains what looks like a committed credential value ({match.Value})");
            }
        }
    }

    private static bool IsPlaceholder(string value) =>
        PlaceholderValues.Contains(value, StringComparer.OrdinalIgnoreCase)
        || (value.StartsWith('<') && value.EndsWith('>'))
        || value.StartsWith("REPLACE", StringComparison.OrdinalIgnoreCase)
        || ShellVariableReferenceOnly.IsMatch(value);

    /// <summary>
    /// Walks up from the test binary's directory to find the repository root
    /// (identified by <c>CentralLogin.sln</c>), so the check works regardless
    /// of the working directory the test runner was invoked from.
    /// </summary>
    private static string FindKeycloakDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "CentralLogin.sln")))
            {
                continue;
            }

            var keycloakDirectory = Path.Combine(directory.FullName, "keycloak");
            if (Directory.Exists(keycloakDirectory))
            {
                return keycloakDirectory;
            }

            throw new DirectoryNotFoundException(
                $"Found repository root at '{directory.FullName}' but it has no keycloak/ directory.");
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root (CentralLogin.sln) from the test binary's directory "
                + $"('{AppContext.BaseDirectory}') or any of its parents.");
    }
}
