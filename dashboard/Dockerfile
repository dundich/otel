# Custom Aspire Dashboard with the "Управление" tab.
#
# Sources live in dashboard/aspire (vendored Aspire 17.0.0). They are built here;
# apps remain .NET 10, only the dashboard uses .NET 11 because Aspire 17's
# dashboard sources target net11.0.
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src

COPY dashboard/aspire/ ./

RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore src/Aspire.Dashboard/Aspire.Dashboard.csproj -p:PublishAot=false

RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish src/Aspire.Dashboard/Aspire.Dashboard.csproj \
    -c Release \
    -o /out \
    -p:PublishAot=false \
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
ENTRYPOINT ["dotnet", "Aspire.Dashboard.dll"]
