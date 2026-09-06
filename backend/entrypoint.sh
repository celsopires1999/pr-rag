#!/bin/sh
set -eu

if [ "$(id -u)" = "0" ]; then
    mkdir -p /app/reports
    chown -R app:app /app/reports
    exec su app -s /bin/sh -c 'exec dotnet /app/PrRag.Api.dll'
fi

exec dotnet /app/PrRag.Api.dll