# .NET 9 base. The .NET 10 SDK (required by global.json) and Docker (for the e2e
# VHS recording) are layered in as devcontainer features — see devcontainer.json.
# No bookworm-based .NET 10 image is published, so we add the SDK rather than
# switch the distro.
FROM mcr.microsoft.com/devcontainers/dotnet:1-9.0-bookworm

RUN rm -f /etc/apt/sources.list.d/yarn.list \
 && rm -f /usr/share/keyrings/yarnkey.gpg \
 && apt-get update
