# Build context: repository root (see compose.yaml).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/JobRadar.Contracts/*.csproj src/JobRadar.Contracts/
COPY src/JobRadar.ServiceDefaults/*.csproj src/JobRadar.ServiceDefaults/
COPY src/JobRadar.Feeds/*.csproj src/JobRadar.Feeds/
COPY src/JobRadar.Feeds.Worker/*.csproj src/JobRadar.Feeds.Worker/
RUN dotnet restore src/JobRadar.Feeds.Worker/JobRadar.Feeds.Worker.csproj
COPY src/ src/
RUN dotnet publish src/JobRadar.Feeds.Worker/JobRadar.Feeds.Worker.csproj -c Release --no-restore -o /app -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# Invented fixtures so the demo runs offline (Feeds__Mode=Fixtures); Live mode ignores them.
COPY tests/fixtures/feeds/ fixtures/
RUN mkdir -p /app/state && chown "$APP_UID" /app/state
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Feeds__FixturesPath=/app/fixtures \
    Feeds__StatePath=/app/state/feeds.json
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "JobRadar.Feeds.Worker.dll"]
