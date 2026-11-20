# Build context: repository root (see compose.yaml).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/JobRadar.Contracts/*.csproj src/JobRadar.Contracts/
COPY src/JobRadar.ServiceDefaults/*.csproj src/JobRadar.ServiceDefaults/
COPY src/JobRadar.Catalog/*.csproj src/JobRadar.Catalog/
COPY src/JobRadar.Matching/*.csproj src/JobRadar.Matching/
COPY src/JobRadar.Tracker/*.csproj src/JobRadar.Tracker/
COPY src/JobRadar.Api/*.csproj src/JobRadar.Api/
RUN dotnet restore src/JobRadar.Api/JobRadar.Api.csproj
COPY src/ src/
RUN dotnet publish src/JobRadar.Api/JobRadar.Api.csproj -c Release --no-restore -o /app -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "JobRadar.Api.dll"]
