# Development container for PrRag (.NET 10).
# Built on the official .NET devcontainer image (includes the .NET 10 SDK,
# git, curl, and common tooling). The Dockerfile adds a few utilities and
# pre-warms the NuGet package cache so the first build inside the container
# is fast.
FROM mcr.microsoft.com/devcontainers/dotnet:1-10.0

# Add a few dev utilities not present in the base image. git/curl/etc. are
# already included by the devcontainers/dotnet image.
#
# The devcontainers/dotnet base image ships a yarn apt source whose key is no
# longer valid, which makes `apt-get update` fail. It is removed here first.
RUN rm -f /etc/apt/sources.list.d/yarn.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends postgresql-client \
    && rm -rf /var/lib/apt/lists/*
