FROM python:3.13-alpine
WORKDIR /app
COPY server.py .
ENV ALERT_SINK_PORT=8089
ENV ALERT_SINK_DATA=/data
VOLUME ["/data"]
EXPOSE 8089
CMD ["python", "-u", "server.py"]
