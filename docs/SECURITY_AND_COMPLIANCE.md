# الأمان والامتثال (ملخص تنفيذي)

## الأسرار وإدارة الهوية

- استخدم `Keycloak__ClientSecret` ومتغيرات بيئة أخرى بدلاً من تخزين الأسرار في الشيفرة. راجع [KEYCLOAK_AND_SECRETS.md](KEYCLOAK_AND_SECRETS.md).

## البيانات الصحية والشكاوى

- صلّح صلاحيات العرض في واجهات HR.Health و Compliance حسب الدور.
- يُستدعى `SensitiveDataAudit.Log` من خدمات حساسة (مثل تسجيل شكوى في Compliance، توصية طبية في HR.Health) ويُخرج سجلات Serilog بخصائص `AuditSensitive` و`AuditDomain` — صُفِّ مخرجات السجلات في الإنتاج واربطها بأداة مراقبة.
- لأسرار البنية التحتية في Azure استخدم **Azure Key Vault** (أو ما يعادله) وحقن القيم كمتغيرات بيئة؛ راجع [KEYCLOAK_AND_SECRETS.md](KEYCLOAK_AND_SECRETS.md).

## النسخ الاحتياطي

- نفّذ نسخاً احتياطياً دورياً لكل قاعدة PostgreSQL خدمة واختبر الاستعادة؛ إرشادات مختصرة: [BACKUP_POSTGRES.md](BACKUP_POSTGRES.md).

## الامتثال القانوني

- حدّد الولاية القضائية (محلي / GDPR) وطبّق سياسات الاحتفاظ والحذف وفقاً لسياسة المؤسسة.

## التتبع بين الخدمات

- رأس `X-Correlation-ID` يُمرَّر عبر البوابة والخدمات ويُسجَّل في Serilog عبر خاصية `CorrelationId`.
