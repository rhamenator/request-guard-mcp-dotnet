using RequestGuardMcp.Core.Errors;

namespace RequestGuardMcp.Tests.Errors;

public class AppErrorExceptionTests
{
    [Theory]
    [InlineData(AppErrorKind.Unauthenticated, 401, "UNAUTHENTICATED")]
    [InlineData(AppErrorKind.Forbidden, 403, "FORBIDDEN")]
    [InlineData(AppErrorKind.ToolNotFound, 404, "TOOL_NOT_FOUND")]
    [InlineData(AppErrorKind.InvalidRequest, 400, "INVALID_REQUEST")]
    [InlineData(AppErrorKind.Validation, 400, "VALIDATION_FAILED")]
    [InlineData(AppErrorKind.RateLimitExceeded, 429, "RATE_LIMIT_EXCEEDED")]
    [InlineData(AppErrorKind.RequestTooLarge, 413, "REQUEST_TOO_LARGE")]
    [InlineData(AppErrorKind.Timeout, 504, "TIMEOUT")]
    [InlineData(AppErrorKind.Upstream, 502, "UPSTREAM_ERROR")]
    [InlineData(AppErrorKind.IntegrationUnavailable, 502, "INTEGRATION_UNAVAILABLE")]
    [InlineData(AppErrorKind.Serialization, 422, "SERIALIZATION_ERROR")]
    [InlineData(AppErrorKind.Internal, 500, "INTERNAL_ERROR")]
    public void StatusCodeAndCodeMatchKind(AppErrorKind kind, int expectedStatus, string expectedCode)
    {
        var error = kind switch
        {
            AppErrorKind.Unauthenticated => AppErrorException.Unauthenticated(),
            AppErrorKind.Forbidden => AppErrorException.Forbidden(),
            AppErrorKind.ToolNotFound => AppErrorException.ToolNotFound("classify"),
            AppErrorKind.InvalidRequest => AppErrorException.InvalidRequest("bad"),
            AppErrorKind.Validation => AppErrorException.Validation("bad"),
            AppErrorKind.RateLimitExceeded => AppErrorException.RateLimitExceeded(),
            AppErrorKind.RequestTooLarge => AppErrorException.RequestTooLarge(),
            AppErrorKind.Timeout => AppErrorException.Timeout(),
            AppErrorKind.Upstream => AppErrorException.Upstream("down"),
            AppErrorKind.IntegrationUnavailable => AppErrorException.IntegrationUnavailable("off"),
            AppErrorKind.Serialization => AppErrorException.Serialization("bad json"),
            AppErrorKind.Internal => AppErrorException.Internal(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        Assert.Equal(expectedStatus, error.StatusCode);
        Assert.Equal(expectedCode, error.Code);
    }

    [Fact]
    public void BatchTooLargeReportsMaxAndGot()
    {
        var error = AppErrorException.BatchTooLarge(max: 50, got: 75);

        Assert.Equal(413, error.StatusCode);
        Assert.Equal("BATCH_TOO_LARGE", error.Code);
        Assert.Equal(50, error.BatchMax);
        Assert.Equal(75, error.BatchGot);
    }
}
