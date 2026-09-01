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

# Run as a non-root user (security best practice). The .NET images ship an
# unprivileged `app` user (UID $APP_UID, 1654 by default) built for exactly
# this purpose. Keep it in sync with the host ownership required for the
# ./reports and ./data bind mounts (see docker-compose.yml / README).
RUN mkdir -p /app/reports \
    && chown -R app:app /app

COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "PrRag.Api.dll"]
