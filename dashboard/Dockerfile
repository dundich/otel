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

ENV ASPNETCORE_URLS=http://0.0.0.0:18888
EXPOSE 18888 18889 18890
USER $APP_UID
ENTRYPOINT ["dotnet", "Aspire.Dashboard.dll"]
