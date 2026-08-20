# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY StatsHub.Api.csproj .
RUN dotnet restore StatsHub.Api.csproj

COPY . .
RUN dotnet publish StatsHub.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

# Railway (and similar hosts) inject $PORT at runtime; bind to it if set,
# otherwise fall back to 8080 for a plain `docker run`.
ENTRYPOINT ["/bin/sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} dotnet StatsHub.Api.dll"]
