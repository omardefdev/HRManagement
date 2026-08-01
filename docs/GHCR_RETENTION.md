# سياسة احتفاظ GHCR (SUP-001-B)

| العنصر | السياسة |
|---|---|
| وسوم الإصدار | `${service}:${SHA12}` فقط — ممنوع `:latest` (DEP-001) |
| Artifacts digests في Actions | احتفاظ **90 يوماً** (كما في `deploy.yml` upload-artifact) |
| صور SHA على GHCR | الاحتفاظ بآخر **90 يوماً** من الوسوم النشطة؛ التنظيف اليدوي/Org settings |
| SBOM / attestations | تُرفع مع كل build-and-push وترتبط بنفس `IMAGE_TAG` |
| Rollback | عبر `action=rollback` + `rollback_tag` لوسوم SHA سابقة ضمن نافذة الاحتفاظ |

مرجع: [ADR-SUP-001-supply-chain.md](./ADR-SUP-001-supply-chain.md)، [DEP_001_RELEASE_GATE.md](./DEP_001_RELEASE_GATE.md).
