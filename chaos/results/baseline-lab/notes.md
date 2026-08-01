# NFR-002 baseline lab

Harness + code gates جاهزة. نفّذ game day على staging مع Toxiproxy واملأ صفاً في `docs/NFR_GAME_DAY_CALENDAR.md`.

## فرضية القالب

> Latency 1.5s على Rabbit يبطئ المستهلكين ويرفع عمر outbox دون فقد أحداث outbox؛ بعد restore يعود freshness.

## خطوات

1. Compose chaos profile up
2. inject rabbit latency
3. راقب مقاييس / أطلق fire-drill إن لزم
4. restore
5. احفظ summary تحت مجلد ختم زمني جديد
