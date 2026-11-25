using System.Text.RegularExpressions;

namespace RequestGuardMcp.Host;

/// <summary>
/// Last line of defence for startup errors (issue 85): even if an exception message carries configuration text, URI
/// credentials, connection-string secrets and bearer tokens are masked before it reaches stderr or a log.
/// </summary>
internal static partial class StartupErrorText
{
    internal static string Scrub(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return string.Empty;
        }

        message = UriUserInfo().Replace(message, "$1***@");
        message = KeyValueSecret().Replace(message, "$1=***");
        return BearerToken().Replace(message, "Bearer ***");
    }

    [GeneratedRegex(@"([A-Za-z][A-Za-z0-9+.\-]*://)[^/\s:@]*:[^/\s@]*@", RegexOptions.NonBacktracking | RegexOptions.CultureInvariant)]
    private static partial Regex UriUserInfo();

    [GeneratedRegex(@"\b(password|pwd|passwd|secret|token|api[_\-]?key|access[_\-]?key|auth[_\-]?token)\s*=\s*[^;\s,]+", RegexOptions.NonBacktracking | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KeyValueSecret();

    [GeneratedRegex(@"\bbearer\s+[A-Za-z0-9._~+/=\-]+", RegexOptions.NonBacktracking | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerToken();
}
