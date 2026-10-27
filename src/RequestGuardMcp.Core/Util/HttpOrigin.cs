namespace RequestGuardMcp.Core.Util;

/// <summary>Canonical bare HTTP(S) origin; never a URL with userinfo, a path or a wildcard.</summary>
public static class HttpOrigin
{
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var separator = value.IndexOf("://", StringComparison.Ordinal);
        if (separator < 0 || value.Any(char.IsWhiteSpace) || value.Contains('\\') || value.Contains('*')
            || value[(separator + 3)..].IndexOfAny(['/', '?', '#', '@', '%']) >= 0
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || uri.HostNameType == UriHostNameType.Unknown
            || uri.UserInfo.Length > 0)
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }
}
