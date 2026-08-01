# فحص الملفات والحجر وCDR (FILE-002)

## ScanStatus (منفصل عن دورة الحياة)

| ScanStatus | المعنى |
|---|---|
| `PendingScan` | مرفوع إلى الحجر؛ بانتظار الماسح |
| `Clean` | نُقّي وفُحص؛ متاح للتحميل/presign |
| `Infected` | اكتشف الماسح تهديداً؛ يبقى في الحجر |
| `Quarantined` | سياسة CDR/محتوى نشط حجب الترقية |
| `ScanError` | فشل الماسح بعد الاستنفاد (فشل مغلق) |

`FileRecordStatus` (`Pending`/`Active`/`PendingPurge`/`Deleted`) يبقى كما في FILE-001.

## مسار الرفع

1. تحقق امتداد/حجم + **magic bytes** (`FileContentProbe`) + حدود أرشيف Office.
2. تخزين في `file-storage-quarantine`.
3. `ScanStatus = PendingScan` + `FileScanWorkItem`.
4. `FileScanProcessorJob` / `POST /api/files/admin/process-scan`:
   - EICAR ثم ClamAV (`IMalwareScanner` / `CompositeMalwareScanner`).
   - Infected → حجر.
   - Error → إعادة محاولة ثم `ScanError` (**لا Clean**).
   - Clean → CDR خفيف → ترقية إلى `file-storage` + تحديث `Sha256`/`Bucket`.

## منع التحميل

`Download` و`PresignedUrl` يتطلبان `ScanStatus == Clean`؛ وإلا 409 عربي.

## ClamAV

| إعداد | المعنى |
|---|---|
| `FileStorage:ClamAvHost` | مضيف clamd |
| `FileStorage:ClamAvPort` | افتراضي 3310 |
| `FileStorage:ClamAvRequired` | عند `false` وبدون host → مسار EICAR فقط (تطوير/اختبار) |

الإنتاج: `ClamAvRequired=true` + خدمة clamav.

## CDR الخفيف

- JPEG/PNG: إسقاط metadata
- docx/xlsx: إزالة macros (`vbaProject.bin`)
- PDF: رفض محتوى نشط (JS/Embedded/Launch)

نتائج الفحص تُسجَّل في `FileScanResults` (append-only) مع مرآة على `FileRecord`.

## ما خارج النطاق

- SEC-003 تفويض كائني
- جهاز CDR مؤسسي
- دعم `.zip` عام للمستخدمين
