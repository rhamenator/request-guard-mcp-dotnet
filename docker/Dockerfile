FROM mcr.microsoft.com/dotnet/sdk:10.0.403-noble@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b AS build
ARG VERSION=0.1.0
WORKDIR /src
COPY . .
RUN dotnet restore RequestGuardMcp.slnx --locked-mode \
    && dotnet publish src/RequestGuardMcp.Host/RequestGuardMcp.Host.csproj \
       --configuration Release --no-restore --output /app \
       /p:UseAppHost=false /p:Version=$VERSION

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14-noble-chiseled@sha256:c89a6a61f494f20a5ae302990e814627a8d91b332fd4a8e7c1707c2d4da9555d AS runtime
ARG GIT_COMMIT=unknown
ARG BUILD_DATE=unknown
ARG VERSION=0.1.0
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
