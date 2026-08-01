# Runbook إشعار تغيير الإعداد القانوني (GOV-002)

## الترتيب الإلزامي

1. تعديل مسودة أو clone من نسخة immutable.
2. **اعتماد تشغيلي** (`Approve`) — PAY-002.
3. **تصديق قانوني** (`LegalAttest`) — يثبت `decisionIds` + `ContentHash`.
4. **نشر** (`Publish`) — يتطلب attestation سارية لنفس الـhash؛ ينشئ `PayrollConfigChangeNotice`.
5. التحقق من `IsActive` للنسخة المنشورة.

## محتوى الإشعار

- `PayrollConfigurationId`, `SupersedesConfigurationId`, `ContentHash`, `ImpactSummary`, `PublishedAt`, `PublishedBy`.

## أدلة تدقيق

- صف `LegalAttestations` بحالة Active.
- صف `PayrollConfigChangeNotices` بعد النشر.
- تشغيلات لاحقة تثبت الـhash الجديد فقط؛ القديمة دون تغيير.
