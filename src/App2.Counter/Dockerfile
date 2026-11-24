FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY src/Dashboard.Client/Dashboard.Client.csproj src/Dashboard.Client/
COPY src/App2.Counter/App2.Counter.csproj src/App2.Counter/
RUN dotnet restore src/App2.Counter/App2.Counter.csproj

COPY src/ src/
RUN dotnet publish src/App2.Counter/App2.Counter.csproj -c Release -o /out --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "App2.Counter.dll"]
