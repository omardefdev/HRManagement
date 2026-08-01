#!/usr/bin/env python3
"""OBS-002 alert-sink: receive Alertmanager webhooks; expose GET /last and /health."""
from __future__ import annotations

import json
import os
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path
from threading import Lock

PORT = int(os.environ.get("ALERT_SINK_PORT", "8089"))
DATA_DIR = Path(os.environ.get("ALERT_SINK_DATA", "/data"))
JSONL = DATA_DIR / "alerts.jsonl"
LAST = DATA_DIR / "last.json"
LOCK = Lock()


class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt: str, *args) -> None:  # quieter
        pass

    def _send(self, code: int, body: bytes, content_type: str = "application/json") -> None:
        self.send_response(code)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self) -> None:  # noqa: N802
        if self.path in ("/health", "/health/ready", "/"):
            self._send(200, b'{"status":"ok"}')
            return
        if self.path == "/last":
            if LAST.exists():
                self._send(200, LAST.read_bytes())
            else:
                self._send(404, b'{"error":"no alerts yet"}')
            return
        self._send(404, b'{"error":"not found"}')

    def do_POST(self) -> None:  # noqa: N802
        if self.path not in ("/", "/webhook", "/alerts"):
            self._send(404, b'{"error":"not found"}')
            return
        length = int(self.headers.get("Content-Length", "0"))
        raw = self.rfile.read(length) if length else b"{}"
        try:
            payload = json.loads(raw.decode("utf-8") or "{}")
        except json.JSONDecodeError:
            payload = {"raw": raw.decode("utf-8", errors="replace")}
        record = {
            "received_at": datetime.now(timezone.utc).isoformat(),
            "payload": payload,
        }
        line = (json.dumps(record, ensure_ascii=False) + "\n").encode("utf-8")
        with LOCK:
            DATA_DIR.mkdir(parents=True, exist_ok=True)
            with JSONL.open("ab") as f:
                f.write(line)
            LAST.write_text(json.dumps(record, ensure_ascii=False, indent=2), encoding="utf-8")
        self._send(200, b'{"status":"accepted"}')


def main() -> None:
    DATA_DIR.mkdir(parents=True, exist_ok=True)
    server = HTTPServer(("0.0.0.0", PORT), Handler)
    print(f"alert-sink listening on :{PORT}", flush=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
