# Platform API image: build with the .NET SDK, run on the ASP.NET Core runtime as a non-root user.
# Base images are pinned by tag and digest. The SDK tag matches api/global.json; the runtime is the
# one that ships with that SDK.

FROM mcr.microsoft.com/dotnet/sdk:10.0.402@sha256:747cbade5bfd918a175ac32f8f1d931edec2c7d0d5397a3de72eaef1ac1f66d2 AS build
WORKDIR /src

# Restore first so the package layer is cached while only source files change.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Platform.Core/Platform.Core.csproj src/Platform.Core/
COPY src/Platform.Infrastructure/Platform.Infrastructure.csproj src/Platform.Infrastructure/
COPY src/Platform.Api/Platform.Api.csproj src/Platform.Api/
RUN dotnet restore src/Platform.Api/Platform.Api.csproj

COPY src/ src/
RUN dotnet publish src/Platform.Api/Platform.Api.csproj --configuration Release --no-restore --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13@sha256:8af87eba5e7cdafb1514d7388be287365da97c132509e84dfe252ffd552398ca AS runtime
WORKDIR /app
COPY --from=build /app ./

# The base image listens on 8080 (ASPNETCORE_HTTP_PORTS) and provides the non-root APP_UID user.
EXPOSE 8080
USER $APP_UID

# Liveness check without adding curl: a raw HTTP request over bash's /dev/tcp to GET /health.
HEALTHCHECK --interval=10s --timeout=5s --start-period=30s --retries=3 \
  CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health HTTP/1.0\\r\\nHost: localhost\\r\\n\\r\\n' >&3 && grep -q '\"status\":\"ok\"' <&3"]

ENTRYPOINT ["dotnet", "Platform.Api.dll"]
