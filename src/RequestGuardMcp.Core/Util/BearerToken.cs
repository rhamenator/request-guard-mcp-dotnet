namespace RequestGuardMcp.Core.Util;

/// <summary>RFC 6750 token68 alphabet, with optional trailing padding and no whitespace.</summary>
public static class BearerToken
{
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var hasData = false;
        var padding = false;
        foreach (var character in value)
        {
            if (character == '=')
            {
                padding = true;
            }
            else if (!padding && (char.IsAsciiLetterOrDigit(character) || character is '-' or '.' or '_' or '~' or '+' or '/'))
            {
                hasData = true;
            }
            else
            {
                return false;
            }
        }

        return hasData;
    }
}
