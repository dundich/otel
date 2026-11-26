# Custom Aspire Dashboard with the "Управление" tab.
#
# The dashboard is a library (dashboard/aspire); the web host lives in
# dashboard/host and owns configuration. Apps remain .NET 10, only the host and
# dashboard use .NET 11 because Aspire 17 sources target net11.0.
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src

# Optional product version: docker compose build with DASHBOARD_VERSION=1.2.3.
ARG VERSION

# The panels SDK under dashboard/panels uses the repository-wide .NET 10 props
# and central package versions, so copy them next to the dashboard tree.
COPY Directory.Build.props Directory.Packages.props ./
COPY dashboard/ ./

RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore host/RaMonitor.Host.csproj

RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish host/RaMonitor.Host.csproj \
    -c Release \
    -o /out \
    -p:PublishAot=false \
    ${VERSION:+-p:Version=$VERSION} \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS final
WORKDIR /app
COPY --from=build /out .

LABEL org.opencontainers.image.title="РА-Монитор"
LABEL org.opencontainers.image.description="Custom Aspire Dashboard with app panels, file browser and service registry"
LABEL org.opencontainers.image.licenses="MIT"
LABEL org.opencontainers.image.source="https://github.com/dundich/otel"

# License formalities: keep all notices in the shipped image.
COPY LICENSE /app/licenses/LICENSE.txt
COPY NOTICE /app/licenses/NOTICE.txt
COPY THIRD-PARTY-NOTICES.TXT /app/licenses/THIRD-PARTY-NOTICES.txt
COPY dashboard/aspire/LICENSE.TXT /app/licenses/ASPIRE-LICENSE.txt
COPY dashboard/aspire/THIRD-PARTY-NOTICES.TXT /app/licenses/ASPIRE-THIRD-PARTY-NOTICES.txt

ENV ASPNETCORE_URLS=http://0.0.0.0:18888
EXPOSE 18888 18889 18890
USER $APP_UID
ENTRYPOINT ["dotnet", "RaMonitor.Host.dll"]
