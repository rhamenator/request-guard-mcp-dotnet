FROM mcr.microsoft.com/dotnet/sdk:10.0.404-noble@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb AS build
ARG VERSION=0.1.1
WORKDIR /src
COPY . .
RUN dotnet restore RequestGuardMcp.slnx --locked-mode \
    && dotnet publish src/RequestGuardMcp.Host/RequestGuardMcp.Host.csproj \
       --configuration Release --no-restore --output /app \
       /p:UseAppHost=false /p:Version=$VERSION

FROM mcr.microsoft.com/dotnet/aspnet:10.0.15-noble-chiseled@sha256:7c625e44e5d588b4d7b2a913e94ca4bd5098ce47442da404829e4c7936a764e1 AS runtime
ARG GIT_COMMIT=unknown
ARG BUILD_DATE=unknown
ARG VERSION=0.1.1
LABEL org.opencontainers.image.source="https://github.com/rhamenator/request-guard-mcp-dotnet" \
      org.opencontainers.image.revision=$GIT_COMMIT \
      org.opencontainers.image.created=$BUILD_DATE \
      org.opencontainers.image.version=$VERSION \
      org.opencontainers.image.licenses="GPL-3.0-or-later"
WORKDIR /app
COPY --from=build --chown=$APP_UID:$APP_UID /app .
USER $APP_UID
ENV MCP__HOST=0.0.0.0 MCP__PORT=8085 ASPNETCORE_HTTP_PORTS= DOTNET_EnableDiagnostics=0 GIT_COMMIT=$GIT_COMMIT BUILD_DATE=$BUILD_DATE
EXPOSE 8085
ENTRYPOINT ["dotnet", "request-guard-mcp-dotnet.dll"]
