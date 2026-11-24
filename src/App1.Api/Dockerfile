FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY src/Dashboard.Client/Dashboard.Client.csproj src/Dashboard.Client/
COPY src/App1.Api/App1.Api.csproj src/App1.Api/
RUN dotnet restore src/App1.Api/App1.Api.csproj

COPY src/ src/
RUN dotnet publish src/App1.Api/App1.Api.csproj -c Release -o /out --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "App1.Api.dll"]
