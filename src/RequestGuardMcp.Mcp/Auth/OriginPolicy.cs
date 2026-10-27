using Microsoft.AspNetCore.Http;
using RequestGuardMcp.Core.Errors;
using RequestGuardMcp.Core.Util;

namespace RequestGuardMcp.Mcp.Auth;

public static class OriginPolicy
{
    public static void Validate(IHeaderDictionary headers, IReadOnlyList<string> allowed)
    {
        if (!headers.TryGetValue("Origin", out var values))
        {
            return;
        }

        if (values.Count != 1 || values[0] is not { } value || HttpOrigin.Normalize(value) is not { } origin
            || !allowed.Any(candidate => string.Equals(HttpOrigin.Normalize(candidate), origin, StringComparison.Ordinal)))
        {
            throw AppErrorException.Forbidden();
        }
    }
}
