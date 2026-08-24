namespace RequestGuardMcp.Core.Errors;

/// <summary>
/// The discriminant for <see cref="AppErrorException"/>, mirroring the Rust server's <c>AppErrorException</c> enum
/// (src/error.rs) so the two implementations report the same machine-readable error codes.
/// </summary>
public enum AppErrorKind
{
    Unauthenticated,
    Forbidden,
    ToolNotFound,
    InvalidRequest,
    Validation,
    RateLimitExceeded,
    RequestTooLarge,
    BatchTooLarge,
    Timeout,
    Upstream,
    IntegrationUnavailable,
    Serialization,
    Internal,
}

/// <summary>
/// Application-level error thrown by tool implementations and caught at the MCP dispatch
/// boundary, where it is translated into a JSON-RPC error response. Ports src/error.rs.
/// </summary>
public sealed class AppErrorException : Exception
{
    public AppErrorKind Kind { get; }
    private readonly int? _batchMax;
    private readonly int? _batchGot;

    private AppErrorException(AppErrorKind kind, string message, int? batchMax = null, int? batchGot = null)
        : base(message)
    {
        Kind = kind;
        _batchMax = batchMax;
        _batchGot = batchGot;
    }

    public static AppErrorException Unauthenticated() => new(AppErrorKind.Unauthenticated, "authentication required");

    public static AppErrorException Forbidden() => new(AppErrorKind.Forbidden, "forbidden");

    public static AppErrorException ToolNotFound(string name) => new(AppErrorKind.ToolNotFound, $"tool not found: {name}");

    public static AppErrorException InvalidRequest(string detail) => new(AppErrorKind.InvalidRequest, $"invalid request: {detail}");

    public static AppErrorException Validation(string detail) => new(AppErrorKind.Validation, $"validation failed: {detail}");

    public static AppErrorException RateLimitExceeded() => new(AppErrorKind.RateLimitExceeded, "rate limit exceeded");

    public static AppErrorException RequestTooLarge() => new(AppErrorKind.RequestTooLarge, "request too large");

    public static AppErrorException BatchTooLarge(int max, int got) =>
        new(AppErrorKind.BatchTooLarge, $"batch too large: max {max}, got {got}", max, got);

    public static AppErrorException Timeout() => new(AppErrorKind.Timeout, "tool timeout");

    public static AppErrorException Upstream(string detail) => new(AppErrorKind.Upstream, $"upstream error: {detail}");

    public static AppErrorException IntegrationUnavailable(string detail) =>
        new(AppErrorKind.IntegrationUnavailable, $"integration unavailable: {detail}");

    public static AppErrorException Serialization(string detail) => new(AppErrorKind.Serialization, $"serialization error: {detail}");

    public static AppErrorException Internal() => new(AppErrorKind.Internal, "internal error");

    /// <summary>An HTTP-style status code for this error.</summary>
    public int StatusCode => Kind switch
    {
        AppErrorKind.Unauthenticated => 401,
        AppErrorKind.Forbidden => 403,
        AppErrorKind.ToolNotFound => 404,
        AppErrorKind.InvalidRequest or AppErrorKind.Validation => 400,
        AppErrorKind.RateLimitExceeded => 429,
        AppErrorKind.RequestTooLarge or AppErrorKind.BatchTooLarge => 413,
        AppErrorKind.Timeout => 504,
        AppErrorKind.Upstream or AppErrorKind.IntegrationUnavailable => 502,
        AppErrorKind.Serialization => 422,
        AppErrorKind.Internal => 500,
        _ => 500,
    };

    /// <summary>A stable, machine-readable error code.</summary>
    public string Code => Kind switch
    {
        AppErrorKind.Unauthenticated => "UNAUTHENTICATED",
        AppErrorKind.Forbidden => "FORBIDDEN",
        AppErrorKind.ToolNotFound => "TOOL_NOT_FOUND",
        AppErrorKind.InvalidRequest => "INVALID_REQUEST",
        AppErrorKind.Validation => "VALIDATION_FAILED",
        AppErrorKind.RateLimitExceeded => "RATE_LIMIT_EXCEEDED",
        AppErrorKind.RequestTooLarge => "REQUEST_TOO_LARGE",
        AppErrorKind.BatchTooLarge => "BATCH_TOO_LARGE",
        AppErrorKind.Timeout => "TIMEOUT",
        AppErrorKind.Upstream => "UPSTREAM_ERROR",
        AppErrorKind.IntegrationUnavailable => "INTEGRATION_UNAVAILABLE",
        AppErrorKind.Serialization => "SERIALIZATION_ERROR",
        AppErrorKind.Internal => "INTERNAL_ERROR",
        _ => "INTERNAL_ERROR",
    };

    public int? BatchMax => _batchMax;
    public int? BatchGot => _batchGot;
}
