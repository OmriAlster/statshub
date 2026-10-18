# syntax=docker/dockerfile:1
#
# Built with the repo root as the Docker context (see railway.json's
# dockerfilePath) so Railway's GitHub-connected auto-deploys work from this
# monorepo without a separate "root directory" setting.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY backend/StatsHub.Api/StatsHub.Api.csproj backend/StatsHub.Api/
RUN dotnet restore backend/StatsHub.Api/StatsHub.Api.csproj

COPY backend/StatsHub.Api/ backend/StatsHub.Api/
RUN dotnet publish backend/StatsHub.Api/StatsHub.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Npgsql (the Postgres driver) dynamically loads libgssapi during connection
# negotiation even for password auth; the slim aspnet image doesn't ship it,
# which surfaces as random 500s only on requests that open a fresh pooled
# connection. tzdata gives the nightly IBBA sync the Israel time zone
# (Asia/Jerusalem), so it runs at local midnight including across DST.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 tzdata \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

ENV ASPNETCORE_ENVIRONMENT=Production
# Fewer native memory pools (glibc makes one per core by default) - each
# holds on to freed memory, and RAM is what the host bills.
ENV MALLOC_ARENA_MAX=2
EXPOSE 8080

# Railway (and similar hosts) inject $PORT at runtime; bind to it if set,
# otherwise fall back to 8080 for a plain `docker run`.
ENTRYPOINT ["/bin/sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} dotnet StatsHub.Api.dll"]
