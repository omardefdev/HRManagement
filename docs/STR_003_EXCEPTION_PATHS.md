# مسارات الاستثناء — STR-003

| مسار | السلوك |
|---|---|
| وحدة Ready بلا uat/runbook/slo أو ملف مفقود | فشل validate |
| slug Pilot NotReady بلا exception | فشل validate |
| EnabledModules يتضمن NotReady | مرفوض حسب FEATURE_FLAG_GATE |
| empty EnabledModules مع وجود NotReady منشور | مرفوض بلا waiver |
| ادعاء «متاحة» لوحدة خارج Ready | مرفوض في AGENCY_CONTRACT |
| تعديل GatewayModuleCatalog كبديل عن الكتالوج | خارج نطاق هذه البطاقة |
| سوق تراخيص / ONB آلي | خارج النطاق |

راجع [ADR-STR-003](ADR-STR-003-service-module-catalog.md).
