# مسارات الاستثناء — EXT-001

| مسار | السلوك |
|---|---|
| account linking بالاسم فقط | فشل validate |
| revocation خارج SLA بلا تصعيد | فشل validate |
| خلط citizen/employee على نفس principal | فشل / block في fraud harness |
| ادعاء IdP وطني/جهة حي Done | مرفوض — soft blockers |
| ادعاء إغلاق IAM-001 | مرفوض — Blocked |
| ادعاء أسطول WebAuthn إنتاجي Done | مرفوض — خارج النطاق |

راجع [ADR-EXT-001](ADR-EXT-001-gov-identity-federation-assurance.md).
