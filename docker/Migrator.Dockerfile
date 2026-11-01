# PLAN-38 (§1): hr-migrate — the migration bundles of the 15 DbContexts plus the bootstrap scripts. One image serves the
# compose init container hrm-bootstrap (ENTRYPOINT: deploy/bootstrap/bootstrap.sh) and the k8s Job
# deploy/k8s/base/migrate-job.yaml (command: /app/scripts/run-migrations.sh, EFBUNDLE_DIR=/app/efbundles).
#   docker build -f docker/Migrator.Dockerfile -t hr-migrate:local .
# The build context is filtered by docker/Migrator.Dockerfile.dockerignore (the scripts and deploy/bootstrap that the
# service images never need).

ARG SDK_BASE_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.400
ARG RUNTIME_BASE_IMAGE=mcr.microsoft.com/dotnet/runtime-deps:10.0

FROM ${SDK_BASE_IMAGE} AS build
WORKDIR /src
# The bootstrap and migration scripts are copied into the runtime stage only, so editing them never rebuilds the bundles
# (COPY --exclude: Dockerfile frontend 1.19+, built into Docker 29).
COPY --exclude=deploy --exclude=scripts . .
COPY scripts/build-ef-bundles.sh scripts/build-ef-bundles.sh
# Self-contained single-file bundles for linux-x64: no .NET runtime in the final image. Each context has an
# IDesignTimeDbContextFactory, so a bundle never runs the service's Program (no wiring or secrets needed to migrate).
RUN --mount=type=cache,id=hrmanagement-nuget,sharing=locked,target=/root/.nuget/packages \
    sed -i 's/\r$//' scripts/build-ef-bundles.sh \
    && EFBUNDLE_DIR=/out/efbundles bash scripts/build-ef-bundles.sh --self-contained -r linux-x64 \
    && ls -la /out/efbundles

FROM ${RUNTIME_BASE_IMAGE} AS runtime
USER root
# psql 16 (Ubuntu 24.04's postgresql-client) for the idempotent SQL and TEN-002C, curl and jq for the RabbitMQ API.
RUN apt-get -o Acquire::Retries=5 update \
    && apt-get -o Acquire::Retries=5 install -y --no-install-recommends postgresql-client curl jq ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && psql --version
WORKDIR /app
COPY --from=build /out/efbundles/ /app/efbundles/
COPY deploy/bootstrap/ /app/bootstrap/
COPY scripts/run-migrations.sh /app/scripts/run-migrations.sh
COPY scripts/sql/ /app/scripts/sql/
RUN find /app/bootstrap /app/scripts -type f \( -name '*.sh' -o -name '*.sql' -o -name '*.conf' -o -name '*.json' \) \
        -exec sed -i 's/\r$//' {} + \
    && chmod 0755 /app/bootstrap/bootstrap.sh /app/scripts/run-migrations.sh /app/efbundles/* \
    && mkdir -p /var/hrm/bootstrap && chown "$APP_UID:$APP_UID" /var/hrm/bootstrap
# Single-file bundles extract native parts under this directory (a read-only root file system keeps /tmp writable).
ENV EFBUNDLE_DIR=/app/efbundles \
    DOTNET_BUNDLE_EXTRACT_BASE_DIR=/tmp/.net \
    HRM_BOOTSTRAP_RESULT_DIR=/var/hrm/bootstrap
LABEL org.opencontainers.image.title="hr-migrate"
USER $APP_UID
ENTRYPOINT ["bash", "/app/bootstrap/bootstrap.sh"]
