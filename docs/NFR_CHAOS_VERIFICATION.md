# NFR-002 — تحقق الفوضى والتعافي

القرار: [ADR-NFR-002-chaos-recovery.md](./ADR-NFR-002-chaos-recovery.md). الكتالوج: [NFR_CHAOS_CATALOG.md](./NFR_CHAOS_CATALOG.md).

## أوامر

```powershell
docker compose -f docker-compose.yml -f docker-compose.chaos.yml --profile chaos up -d toxiproxy toxiproxy-bootstrap

powershell -File scripts/nfr-chaos-inject.ps1 -Target rabbit -Fault latency -LatencyMs 1500
# ... exercise app / watch alerts ...
powershell -File scripts/nfr-chaos-restore.ps1 -Target rabbit

powershell -File scripts/nfr-chaos-disk-pressure.ps1 -MaxMb 32
powershell -File scripts/nfr-chaos-secret-rotation.ps1 -DryRun
powershell -File scripts/nfr-chaos-expired-cert.ps1
powershell -File scripts/nfr-chaos-safe-abort.ps1
```

```bash
bash scripts/nfr-chaos-inject.sh pg down
bash scripts/nfr-chaos-restore.sh pg
bash scripts/nfr-chaos-worker-crash.sh
DRY_RUN=1 bash scripts/obs-alert-fire-drill.sh   # قناة تنبيه أثناء/بعد الحقن
```

اختبارات وحدة:

```bash
dotnet test --filter "FullyQualifiedName~Nfr002"
```

## معايير النجاح

| فحص | نجاح |
|---|---|
| inject → restore | proxies enabled؛ لا toxics متبقية |
| out-of-order tests | أخضر |
| health probe CB مسجّل | `Nfr002CircuitBreakerTests` |
| MassTransit RetryUseJitter | افتراضي true في الكود/وثيقة |
| game day صف واحد موقّع كقالب | [baseline-lab](../chaos/results/baseline-lab/) |
| لا فساد صامت | outbox/inbox/PAY-003 invariants في notes |

## إعادة الاختبار

بعد أي إصلاح من game day: أعد السيناريو نفسه خلال أسبوعين وسجّل في [NFR_GAME_DAY_CALENDAR.md](./NFR_GAME_DAY_CALENDAR.md).
