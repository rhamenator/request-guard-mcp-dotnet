using RequestGuardMcp.Core.Configuration;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Mcp;
using RequestGuardMcp.Mcp.Registry;
using RequestGuardMcp.Mcp.Transport;
using RequestGuardMcp.Tools;

var builder = WebApplication.CreateBuilder(args);

// Environment variables with prefix MCP__ (e.g. MCP__AUTH__ENABLED), matching the Rust server's
// `config::Environment::with_prefix("MCP").separator("__")` (src/config.rs).
builder.Configuration.AddEnvironmentVariables(prefix: "MCP__");

var appConfig = new AppConfig();
builder.Configuration.Bind(appConfig);

// A handful of plain (unprefixed) env vars are also honored for compatibility with the sibling
// Rust project's deployment examples (src/config.rs's AUTH_TOKENS/LOG_LEVEL handling).
if (Environment.GetEnvironmentVariable("AUTH_TOKENS") is { Length: > 0 } rawTokens)
{
    var tokens = rawTokens.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    if (tokens.Length > 0)
    {
        appConfig.Auth.Tokens = [.. tokens];
    }
}

if (Environment.GetEnvironmentVariable("CACHE_SCOPE_HMAC_KEY") is { Length: > 0 } hmacKey)
{
    appConfig.Auth.CacheScopeHmacKey = hmacKey;
}

if (Environment.GetEnvironmentVariable("LOG_LEVEL") is { Length: > 0 } logLevel)
{
    appConfig.LogLevel = logLevel;
}

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

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

builder.Services.AddSingleton(appConfig);
builder.Services.AddSingleton(new AppState(appConfig));
builder.Services.AddSingleton<WebSocketConnectionHandler>();
builder.Services.AddSingleton<McpDispatcher>();

builder.Services.AddSingleton(_ =>
{
    var registry = new ToolRegistry();
    registry.Register(new HealthTool());
    registry.Register(new ModelInfoTool(registry));
    registry.Register(new ClassifyTool());
    registry.Register(new BatchClassifyTool());
    registry.Register(new ExplainTool());
    registry.Register(new ScoreBreakdownTool());
    registry.Register(new ValidatePayloadTool());
    registry.Register(new FeatureFlagsTool());
    registry.Register(new RedactPreviewTool());
    registry.Register(new ConfigSnapshotTool());
    registry.Register(new SelfTestTool());
    registry.Register(new WarmupTool());
    return registry;
});

var app = builder.Build();

app.UseWebSockets();
app.MapMcpServer();

RequestGuardMcp.Host.StartupLog.ServerStarting(app.Logger, appConfig.Host, appConfig.Port);

app.Run();
return 0;
