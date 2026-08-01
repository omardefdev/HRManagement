# مسارات الاستثناء — MIG-006

| مسار | السلوك |
|---|---|
| أقل من 3 rehearsals موقوتة | فشل validate |
| rollback ورقي فقط | فشل validate |
| فقد delta | فشل validate |
| Go بلا استيفاء المعايير | فشل / No-Go |
| legacy غير read-only / غير مؤمّن | فشل validate |
| ادعاء قطع حي / DNS إنتاج Done | مرفوض — خارج النطاق |
| ادعاء إغلاق soft blockers | مرفوض |

راجع [ADR-MIG-006](ADR-MIG-006-cutover-rehearsal-rollback.md).
