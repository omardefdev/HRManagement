# مسارات الاستثناء — FUN-005

| مسار | السلوك |
|---|---|
| تسليم بلا DeliveredTo | رفض |
| تسليم بتوقيع افتراضي «موقّع» | رفض — يتطلب نصاً واضحاً أو FILE-002 |
| تعيين أصل ثانٍ مفتوح | رفض + فهرس فريد |
| أصل Lost/Disposed | لا تعيين |
| أصل IsPrivileged بلا EvidenceRef | رفض تسليم/إعادة/فقدان |
| Directory sync فاشل | ProvisioningStatus=Failed؛ قائمة FailedProvisioning |
| RetryProvision على غير Failed | رفض |
| فصل مع بطاقة Active/Lost | revoke في party ACK |
| تجاوز SLA إبطال 48س | IsRevokeSlaBreached / تدخل محلي |
| soft-cancel طلب بطاقة ثم Process | BadRequest |

راجع [ADR-FUN-005](ADR-FUN-005-identity-assets-it-closure.md).
