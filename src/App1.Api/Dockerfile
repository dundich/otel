FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY dashboard/panels/RaMonitor.Panels.csproj dashboard/panels/
COPY src/App1.Api/App1.Api.csproj src/App1.Api/
RUN dotnet restore src/App1.Api/App1.Api.csproj

COPY src/ src/
COPY dashboard/panels/ dashboard/panels/
RUN dotnet publish src/App1.Api/App1.Api.csproj -c Release -o /out --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /out .

LABEL org.opencontainers.image.title="РА-Монитор app1"
LABEL org.opencontainers.image.licenses="MIT"
LABEL org.opencontainers.image.source="https://github.com/dundich/otel"

COPY LICENSE /app/licenses/LICENSE.txt
COPY NOTICE /app/licenses/NOTICE.txt
COPY THIRD-PARTY-NOTICES.TXT /app/licenses/THIRD-PARTY-NOTICES.txt

ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "App1.Api.dll"]
