# NFR-002 — طوبولوجيا الفوضى المخبرية

## المكوّنات

```text
Apps  →  Toxiproxy (:8474 API)
            ├─ listen :15432 → postgresql:5432
            ├─ listen :15672 → rabbitmq:5672
            └─ listen :15000 → gateway:8080   (اختياري)
chaos/results/<stamp>-<scenario>/
```

على المضيف المحلي الافتراضي: Postgres منشور `5433`، Rabbit `5673`. مع profile chaos تُعرَّف بروكسيات داخل شبكة compose؛ التطبيقات التي تريد الحقن تضبط:

| متغير | قيمة عبر proxy |
|---|---|
| `ConnectionStrings__*Db` Host | `toxiproxy` Port `15432` |
| `RabbitMQ__Host` / Port | `toxiproxy` / `15672` |

## أوامر

```powershell
docker compose -f docker-compose.yml -f docker-compose.chaos.yml --profile chaos up -d toxiproxy
powershell -File scripts/nfr-chaos-inject.ps1 -Target pg -Fault latency -LatencyMs 2000
powershell -File scripts/nfr-chaos-restore.ps1 -Target pg
```

```bash
bash scripts/nfr-chaos-inject.sh rabbit down
bash scripts/nfr-chaos-restore.sh rabbit
```

API: `TOXIPROXY_URL` افتراضي `http://localhost:8474`.

## نتائج

كل سيناريو يكتب تحت `chaos/results/<stamp>-<name>/` ملفاً `summary.json` + `notes.md`.
