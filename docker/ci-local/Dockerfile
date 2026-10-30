# QA-034: local Linux image that runs the CI build-and-test steps (scripts/ci/steps.json) like the GitHub
# ubuntu-latest runner does. Built and run by scripts/ci-local.ps1; never deployed.
# Base: allowed in docker/base-image-pins.json; digest recorded 2026-10-30 (Ubuntu 24.04, SDK 10.0.403, pwsh 7.6.8).
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b

# The committed global.json pins SDK 10.0.400 with rollForward=disable: install exactly that SDK next to the image's.
ARG DOTNET_SDK_VERSION=10.0.400
ARG DOCKER_CLI_VERSION=27.5.1
ARG DOCKER_COMPOSE_VERSION=2.32.4
ARG DOCKER_BUILDX_VERSION=0.20.1
ARG ACTIONLINT_VERSION=1.7.7

ENV DEBIAN_FRONTEND=noninteractive \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    PATH="${PATH}:/root/.dotnet/tools"

RUN apt-get update \
 && apt-get install -y --no-install-recommends python3 ca-certificates curl tar gzip xz-utils \
 && rm -rf /var/lib/apt/lists/* \
 && curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
 && bash /tmp/dotnet-install.sh --version "${DOTNET_SDK_VERSION}" --install-dir /usr/share/dotnet --no-path \
 && rm /tmp/dotnet-install.sh \
 && dotnet --list-sdks | grep -q "^${DOTNET_SDK_VERSION} " \
 && curl -fsSL "https://download.docker.com/linux/static/stable/x86_64/docker-${DOCKER_CLI_VERSION}.tgz" \
      | tar -xz -C /usr/local/bin --strip-components=1 docker/docker \
 && mkdir -p /usr/local/lib/docker/cli-plugins \
 && curl -fsSL -o /usr/local/lib/docker/cli-plugins/docker-compose \
      "https://github.com/docker/compose/releases/download/v${DOCKER_COMPOSE_VERSION}/docker-compose-linux-x86_64" \
 && curl -fsSL -o /usr/local/lib/docker/cli-plugins/docker-buildx \
      "https://github.com/docker/buildx/releases/download/v${DOCKER_BUILDX_VERSION}/buildx-v${DOCKER_BUILDX_VERSION}.linux-amd64" \
 && chmod +x /usr/local/lib/docker/cli-plugins/docker-compose /usr/local/lib/docker/cli-plugins/docker-buildx \
 && curl -fsSL "https://github.com/rhysd/actionlint/releases/download/v${ACTIONLINT_VERSION}/actionlint_${ACTIONLINT_VERSION}_linux_amd64.tar.gz" \
      | tar -xz -C /usr/local/bin actionlint \
 && ln -sf "$(command -v pwsh)" /usr/local/bin/powershell \
 && git config --system --add safe.directory '*' \
 && docker --version && docker compose version && docker buildx version && actionlint -version && python3 --version

WORKDIR /work
