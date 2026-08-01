# مسارات الاستثناء — REL-001

| مسار | السلوك |
|---|---|
| Drift staging↔prod غير مدرج في المصفوفة | `rel001-parity-diff` يفشل |
| comparison.status = todo | فشل البوابة حتى match\|approved |
| مسار production dump في seed | رفض صريح |
| Employees فوق max بدون موافقة | رفض seed |
| REL001_ALLOW_PROD_DUMP=1 | رفض reset |
| ترقية digest مختلف عن staging | مخالف DEP-001 / rehearsal |
| وحدة في EnabledModules غير منشورة | ممنوع حسب feature flags |
| TLS staging معطّل بعد REL-001 | يجب استثناء approved جديد في المصفوفة |

راجع [ADR-REL-001](ADR-REL-001-staging-synthetic-parity.md).
