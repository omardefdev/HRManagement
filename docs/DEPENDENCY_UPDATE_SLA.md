# SLA تحديث الاعتماديات (SUP-001-E)

| الشدة | هدف المعالجة | آلية |
|---|---|---|
| Critical | ≤ **7 أيام** تقويمية من اكتشاف advisory / `dotnet list --vulnerable` | Issue بوسم `SUP-001` + `advisory` + `severity:critical` |
| High | ≤ **14 يوماً** | نفس الآلية |
| Medium | ≤ **30 يوماً** أو الدورة التالية | Dependabot PR |
| Low | أفضل جهد | Dependabot |

استثناء موقّت Crit/High يتطلب صفاً في `supply-chain-exceptions.json` بتاريخ انتهاء ≤ SLA أعلاه ما لم يُبرَّر قانونياً.

مرجع: [ADR-SUP-001-supply-chain.md](./ADR-SUP-001-supply-chain.md).
