using RequestGuardMcp.Core.Configuration;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Mcp;
using RequestGuardMcp.Mcp.Transport;
using RequestGuardMcp.Tools;
using RequestGuardMcp.Integrations;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Local development convenience. Process environment variables still win because they are added
// afterward; production deployments should inject secrets rather than mounting this file.
builder.Configuration.AddIniFile(".env", optional: true, reloadOnChange: false);
builder.Configuration.AddEnvironmentVariables();

// Environment variables with prefix MCP__ (e.g. MCP__AUTH__ENABLED), matching the Rust server's
// `config::Environment::with_prefix("MCP").separator("__")` (src/config.rs).
builder.Configuration.AddEnvironmentVariables(prefix: "MCP__");

var appConfig = new AppConfig();
builder.Configuration.Bind(appConfig);

// A handful of plain (unprefixed) env vars are also honored for compatibility with the sibling
// Rust project's deployment examples (src/config.rs's AUTH_TOKENS/LOG_LEVEL handling).
if (builder.Configuration["AUTH_TOKENS"] is { Length: > 0 } rawTokens)
{
    var tokens = rawTokens.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    if (tokens.Length > 0)
    {
        appConfig.Auth.Tokens = [.. tokens];
    }
}

if (builder.Configuration["CACHE_SCOPE_HMAC_KEY"] is { Length: > 0 } hmacKey)
{
    appConfig.Auth.CacheScopeHmacKey = hmacKey;
}

if (builder.Configuration["LOG_LEVEL"] is { Length: > 0 } logLevel)
{
    appConfig.LogLevel = logLevel;
}

if (builder.Configuration["TLS_FINGERPRINT_ATTESTATION_KEY"] is { Length: > 0 } tlsKey)
{
    appConfig.TlsFingerprints.AttestationKey = tlsKey;
}

if (builder.Configuration["TLS_FINGERPRINT_ATTESTATION_PREVIOUS_KEY"] is { Length: > 0 } previousTlsKey)
{
    appConfig.TlsFingerprints.PreviousAttestationKey = previousTlsKey;
}

if (int.TryParse(builder.Configuration["TLS_FINGERPRINT_ATTESTATION_MAX_AGE_SECONDS"], System.Globalization.CultureInfo.InvariantCulture, out var tlsMaxAge))
{
    appConfig.TlsFingerprints.MaxAgeSeconds = tlsMaxAge;
}

appConfig.TlsFingerprints.KnownBadJa3 = SplitList(builder.Configuration["TLS_KNOWN_BAD_JA3"]);
appConfig.TlsFingerprints.KnownBadJa4 = SplitList(builder.Configuration["TLS_KNOWN_BAD_JA4"]);

try
{
    appConfig.Validate();
}
catch (ConfigValidationException ex)
{
    Console.Error.WriteLine($"configuration error: {ex.Message}");
    return 1;
}

builder.WebHost.UseUrls($"http://{appConfig.Host}:{appConfig.Port}");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = appConfig.Limits.MaxRequestBytes);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Logging.SetMinimumLevel(ParseLogLevel(appConfig.LogLevel));

var telemetry = builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(appConfig.Telemetry.ServiceName))
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation().AddSource("RequestGuardMcp"));
if (appConfig.Telemetry.OtlpEndpoint is { } otlpEndpoint)
{
    telemetry.WithTracing(tracing => tracing.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint)));
}

var appState = new AppState(appConfig);
try
{
    if (appConfig.Redis.Url is not null)
    {
        var redis = await RedisClient.ConnectAsync(appConfig.Redis).ConfigureAwait(false);
        appState.AttachIntegrations(redis: redis);
    }

    if (appConfig.Postgres.Url is not null)
    {
        var postgres = await PostgresClient.ConnectAsync(appConfig.Postgres).ConfigureAwait(false);
        appState.AttachIntegrations(postgres: postgres);
    }

    if (appConfig.Geoip.MmdbPath is not null || appConfig.Geoip.CityMmdbPath is not null || appConfig.Geoip.AsnMmdbPath is not null || appConfig.Geoip.AnonymousIpMmdbPath is not null)
    {
        var geoip = GeoipClient.Load(appConfig.Geoip);
        appState.AttachIntegrations(geoip: geoip);
    }
}
catch (Exception error)
{
    Console.Error.WriteLine($"integration initialization error: {error.Message}");
    await appState.DisposeAsync().ConfigureAwait(false);
    return 1;
}

builder.Services.AddSingleton(appConfig);
builder.Services.AddSingleton(appState);
builder.Services.AddSingleton<WebSocketConnectionHandler>();
builder.Services.AddSingleton<McpDispatcher>();

builder.Services.AddSingleton(_ => ToolRegistryFactory.Create());

var app = builder.Build();

app.UseWebSockets();
app.MapMcpServer(appConfig.Telemetry.MetricsPath);

RequestGuardMcp.Host.StartupLog.ServerStarting(app.Logger, appConfig.Host, appConfig.Port);

app.Run();
return 0;

static List<string> SplitList(string? value) => value?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList() ?? [];

static LogLevel ParseLogLevel(string value) => value.Trim().ToLowerInvariant() switch
{
    "trace" => LogLevel.Trace,
    "debug" => LogLevel.Debug,
    "info" or "information" => LogLevel.Information,
    "warn" or "warning" => LogLevel.Warning,
    "error" => LogLevel.Error,
    "critical" or "fatal" => LogLevel.Critical,
    "none" => LogLevel.None,
    _ => LogLevel.Information,
};
