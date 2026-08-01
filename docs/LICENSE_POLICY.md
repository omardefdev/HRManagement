# سياسة الترخيص (SUP-001-E)

| القاعدة | التفصيل |
|---|---|
| مصدر الحقيقة اليدوي | [`THIRD_PARTY_LICENSES.md`](./THIRD_PARTY_LICENSES.md) |
| بوابة CI | [`scripts/check-license-policy.sh`](../scripts/check-license-policy.sh) |
| مرفوض دون استثناء | GPL / AGPL (والمتغيرات الشائعة) في المخزون |
| استثناء | صف في `supply-chain-exceptions.json` بـ `packageOrCve`: `license:<id>` + تاريخ انتهاء + سبب قانوني |
| مسموح عموماً | MIT، Apache-2.0، BSD، ISC، .NET BCL |

مرجع: [ADR-SUP-001-supply-chain.md](./ADR-SUP-001-supply-chain.md).
