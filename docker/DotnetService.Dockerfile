# syntax=docker/dockerfile:1.4
# BLD-002: unified microservice build — layer-cached restore + shared NuGet cache mount.

ARG PROJECT_CSPROJ
ARG ASSEMBLY_NAME
ARG NEEDS_KRB5=true
ARG GATEWAY_AUDIT_DIR=false
ARG SDK_BASE_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.400
ARG RUNTIME_BASE_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0
ARG REUSE_LOCAL_RUNTIME_BASE=false

FROM ${SDK_BASE_IMAGE} AS build
ARG PROJECT_CSPROJ
WORKDIR /src

COPY docker/build-manifest/ ./
COPY Directory.Build.props Directory.Packages.props NuGet.config global.json HRManagement.slnx ./

RUN --mount=type=cache,id=hrmanagement-nuget,sharing=locked,target=/root/.nuget/packages \
    dotnet restore "${PROJECT_CSPROJ}" --locked-mode

COPY . .

RUN --mount=type=cache,id=hrmanagement-nuget,sharing=locked,target=/root/.nuget/packages \
    dotnet publish "${PROJECT_CSPROJ}" -c Release --no-restore \
    -o /app/publish /p:UseAppHost=false

FROM ${RUNTIME_BASE_IMAGE} AS runtime
ARG NEEDS_KRB5
ARG GATEWAY_AUDIT_DIR
ARG ASSEMBLY_NAME
ARG REUSE_LOCAL_RUNTIME_BASE
ENV APP_DLL=${ASSEMBLY_NAME}
WORKDIR /app

USER root
RUN if [ "$REUSE_LOCAL_RUNTIME_BASE" = "true" ]; then \
        rm -rf /app/*; \
    else \
        packages="curl" \
        && if [ "$NEEDS_KRB5" = "true" ]; then packages="$packages libgssapi-krb5-2"; fi \
        && apt-get -o Acquire::Retries=5 update \
        && apt-get -o Acquire::Retries=5 install -y --no-install-recommends $packages \
        && rm -rf /var/lib/apt/lists/*; \
    fi \
    && if [ "$GATEWAY_AUDIT_DIR" = "true" ]; then mkdir -p /app/data/security-audit && chown -R $APP_UID:$APP_UID /app/data; fi

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["sh", "-c", "exec dotnet \"$APP_DLL\""]
