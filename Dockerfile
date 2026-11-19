# syntax=docker/dockerfile:1

# Build stage
# Exact 10.0.1xx tag: global.json (latestPatch) never leaves the 10.0.1xx band, and "10.0" floats past it.
# Bump this tag and global.json's sdk.version together.
FROM mcr.microsoft.com/dotnet/sdk:10.0.404 AS build
WORKDIR /src

# Copy solution and project files (global.json pins the SDK).
COPY ["GSS.slnx", "./"]
COPY ["global.json", "./"]
COPY ["Directory.Build.props", "./"]
COPY ["Directory.Build.targets", "./"]
COPY ["Directory.Packages.props", "./"]
COPY ["nuget.config", "./"]
COPY ["src/GSS.Api/GSS.Api.csproj", "src/GSS.Api/"]
COPY ["src/GSS.Business/GSS.Business.csproj", "src/GSS.Business/"]
COPY ["src/GSS.Core/GSS.Core.csproj", "src/GSS.Core/"]
COPY ["src/GSS.Infrastructure/GSS.Infrastructure.csproj", "src/GSS.Infrastructure/"]
COPY ["src/GSS.Util/GSS.Util.csproj", "src/GSS.Util/"]

# Restore dependencies
# Private feed packages need a read:packages token, passed as a build secret so no layer holds it:
#   docker build --secret id=nuget_token,env=NUGET_AUTH_TOKEN .
RUN --mount=type=secret,id=nuget_token,required=false \
    if [ -f /run/secrets/nuget_token ]; then \
        env "NuGetPackageSourceCredentials_baps-apps-packages=Username=docker;Password=$(cat /run/secrets/nuget_token)" \
            dotnet restore "src/GSS.Api/GSS.Api.csproj"; \
    else \
        dotnet restore "src/GSS.Api/GSS.Api.csproj"; \
    fi

# Copy source code
COPY src/ src/

# Build and publish offline (--no-restore) against the restore above.
WORKDIR /src/src/GSS.Api
RUN dotnet publish "GSS.Api.csproj" -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Copy published output
COPY --from=build /app/publish .

# Run as the non-root "app" user that ships with the .NET images
USER $APP_UID

# 8080: plain HTTP, only /internal/health/* answers (healthcheck, TLS-terminating proxy).
# 8443: HTTPS; compose.yaml enables it with a self-signed certificate (host port 7109).
EXPOSE 8080
EXPOSE 8443

ENV ASPNETCORE_URLS=http://+:8080

# Liveness over the HTTP probe port; bash opens the socket since the image has no curl/wget.
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD ["/bin/bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /internal/health/liveness HTTP/1.1\\r\\nHost: localhost\\r\\nConnection: close\\r\\n\\r\\n' >&3 && head -n 1 <&3 | grep -q ' 200 '"]

ENTRYPOINT ["dotnet", "GSS.Api.dll"]
