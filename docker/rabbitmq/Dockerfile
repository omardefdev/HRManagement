# PLAN-38 (§2, D-38-4): hr-rabbitmq — RabbitMQ 3.13 with the delayed-message exchange plugin that MassTransit's
# UseDelayedMessageScheduler needs for the saga timeouts (HR.Contracts hire/separation, HR.Movements transfer/secondment).
#   docker build -f docker/rabbitmq/Dockerfile -t hr-rabbitmq:local docker/rabbitmq
# The base image is pinned by digest and the plugin by SHA-256 (computed once from the official release download,
# reports/plan38-progress.md); a different file fails the build.
FROM rabbitmq:3.13-management-alpine@sha256:606d8c0d6b3c18d1da9afc53bc7cdb2a8d5486df91b5a9830e9e07626c9ae281

ARG DELAYED_PLUGIN_VERSION=3.13.0
ARG DELAYED_PLUGIN_SHA256=3479180df03fa830fd59ccf638a60eee8f0e8261524a8616685522d7b8d9a9ee

RUN set -eu; \
    f="rabbitmq_delayed_message_exchange-${DELAYED_PLUGIN_VERSION}.ez"; \
    wget -q -O "/tmp/$f" "https://github.com/rabbitmq/rabbitmq-delayed-message-exchange/releases/download/v${DELAYED_PLUGIN_VERSION}/$f"; \
    echo "${DELAYED_PLUGIN_SHA256}  /tmp/$f" | sha256sum -c -; \
    install -o rabbitmq -g rabbitmq -m 0644 "/tmp/$f" "/opt/rabbitmq/plugins/$f"; \
    rm -f "/tmp/$f"; \
    rabbitmq-plugins enable --offline rabbitmq_delayed_message_exchange; \
    grep -q rabbitmq_delayed_message_exchange /etc/rabbitmq/enabled_plugins

LABEL org.opencontainers.image.title="hr-rabbitmq"
