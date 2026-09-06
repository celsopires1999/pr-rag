# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY PrRag.sln ./
COPY src/PrRag.Application/PrRag.Application.csproj src/PrRag.Application/
COPY src/PrRag.Infrastructure/PrRag.Infrastructure.csproj src/PrRag.Infrastructure/
COPY src/PrRag.Api/PrRag.Api.csproj src/PrRag.Api/
COPY tools/PrRag.DataGenerator/PrRag.DataGenerator.csproj tools/PrRag.DataGenerator/
RUN dotnet restore src/PrRag.Api/PrRag.Api.csproj

COPY src/ src/
COPY tools/ tools/
RUN dotnet publish src/PrRag.Api/PrRag.Api.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080
# HOME=/tmp works for any UID (debug adapters like vsdbg scribble to $HOME).
ENV HOME=/tmp

# Run as a non-root user (security best practice). The .NET images ship an
# unprivileged `app` user (UID $APP_UID, 1654 by default) built for exactly
# this purpose. Keep it in sync with the host ownership required for the
# ./reports and ./data bind mounts (see docker-compose.yml / README).
# The entrypoint starts as root only to fix ownership of the writable bind
# mounts (Docker may create them owned by root on a fresh clone), then drops
# privileges to `app` before running the app.
# /app gets mode 1777 so in-container debuggers and editors that attach with a
# different UID (e.g. the 1000 range) can still write launch artifacts in the
# working directory; the sticky bit keeps each user from clobbering others' files.
RUN mkdir -p /app/reports \
    && chown -R app:app /app \
    && chmod 1777 /app

COPY --from=build /app/publish .
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh
ENTRYPOINT ["/entrypoint.sh"]
