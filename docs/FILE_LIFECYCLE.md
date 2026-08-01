# دورة حياة الملفات (FILE-001)

## الحالات

| Status | المعنى |
|---|---|
| `Pending` | مرفوع ولم يُربَط بمراجع بعد |
| `Active` | له مرجع واحد على الأقل (أو كان نشطاً) |
| `PendingPurge` | soft-delete؛ كائن التخزين ما زال موجوداً؛ عمل في طابور الحذف |
| `Deleted` | اكتمل حذف الكائن من التخزين |

مصدر الحقيقة للمراجع: صفوف `FileReferences`. الحقل `ReferenceCount` مرآة تُحدَّث ذرياً (DATA-001) وتُصلَّح عبر `ReconcileReferenceCountsAsync`.

## حذف ثنائي المراحل

1. **مرحلة 1** (`TryDeleteWhenUnreferencedAsync` / GC TTL): عند `ReferenceCount = 0` وبدون legal hold/`RetainUntil` → `Status = PendingPurge` + إدراج `FilePurgeWorkItem` — **دون** استدعاء حذف MinIO في نفس الطلب.
2. **مرحلة 2** (`ProcessPurgeQueueAsync` / `FileGarbageCollectorJob`): صرف العمل المستحق → `storage.DeleteAsync` مع backoff أسي → عند النجاح `Status = Deleted` و`ObjectPurgedAt`. عند الفشل إعادة جدولة؛ بعد الحد الأقصى → `Dead`.

**لا يُحذف كائن وله مرجع حي.** إن وُجدت مراجع أثناء الصرف يُلغى العمل ويُعاد الملف لـ`Active` إن لزم.

## Force delete

`DELETE /api/files/{id}/force` يتطلب سياسة `FileStorage.ForceDelete` + سبب تدقيق. يرفض `LegalHold` والمراجع الحية ما لم يُمرَّر `overrideLiveRefs` (نادر وموثّق في `ForceDeleteReason`).

## Legal hold / RetainUntil

حقول على `FileRecord`. أي انتقال لـ`PendingPurge` يُرفض أثناء hold أو قبل `RetainUntil`. إدارة عبر `PUT /api/files/{id}/retention`.

## Outbox تسجيل المراجع (FILE-001-C)

الخدمات الحاملة (HR.Core كمرجع) تكتب `FileReferenceOutbox` في نفس معاملة حفظ الكيان عبر `OutboxFileReferenceClient` / `EntityFileReferenceHelper`. الموزّع `FileReferenceOutboxDispatcherJob` يسلّم Register/Release إلى File.Storage عبر HTTP (Idempotent).

## Orphan reconcile

`POST /api/files/admin/orphan-scan` أو السكربت `scripts/reconcile-file-orphans.ps1`:

- صف DB نشط بلا كائن
- كائن تخزين بلا صف
- انحراف العدّاد↔الصفوف (يُصلَح)
- استثناءات معتمدة في `FileReconcileExceptions`

معيار القبول: صفر غير محلولة أو قائمة استثناء معتمدة فقط.

## Checksum

SHA256 عند الرفع. التحميل عبر `OpenDownloadVerifiedAsync` يفشل مغلقاً عند عدم التطابق للملفات `Active` (افتراضي؛ `FileStorage__Checksum__FailClosed=false` للتحذير فقط).

## ما خارج النطاق

- FILE-002: مكتملة — انظر [FILE_SCANNING.md](FILE_SCANNING.md) (`ScanStatus`، حجر، ClamAV، CDR)
- SEC-003: تفويض كائني / `FileAccessGrant` / BOLA — ما زالت مطلوبة

## ScanStatus (FILE-002)

الرفع يبدأ `PendingScan` في bucket الحجر. التحميل/presign فقط عند `Clean`. انظر `docs/FILE_SCANNING.md`.
