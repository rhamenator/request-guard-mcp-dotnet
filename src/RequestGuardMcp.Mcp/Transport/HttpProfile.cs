using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;

namespace RequestGuardMcp.Mcp.Transport;

internal static class HttpProfile
{
    public static int? RejectStatus(HttpRequest request)
    {
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType)
            || !string.Equals(contentType.MediaType, "application/json", StringComparison.OrdinalIgnoreCase)
            || (contentType.CharSet is { } charset && !string.Equals(charset.Trim('"'), "utf-8", StringComparison.OrdinalIgnoreCase)))
        {
            return StatusCodes.Status415UnsupportedMediaType;
        }

        var accepted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in request.Headers.Accept)
        {
            foreach (var part in (value ?? "").Split(','))
            {
                if (!MediaTypeWithQualityHeaderValue.TryParse(part, out var mediaType))
                {
                    return StatusCodes.Status406NotAcceptable;
                }

                if (mediaType.Quality is null or > 0 && mediaType.MediaType is { } media)
                {
                    accepted.Add(media);
                }
            }
        }

        if (!accepted.Contains("application/json") || !accepted.Contains("text/event-stream"))
        {
            return StatusCodes.Status406NotAcceptable;
        }

        if (request.Headers.TryGetValue("MCP-Protocol-Version", out var versions)
            && (versions.Count != 1 || versions[0] != "2025-06-18"))
        {
            return StatusCodes.Status400BadRequest;
        }

        return null;
    }
}
