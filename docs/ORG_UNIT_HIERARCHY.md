# هيكل الوحدات التنظيمية (PERF-003)

## الصيغة

`MaterializedPath` على `OrgUnit` و`OrgUnitSnapshot`:

```
/{guid}/{guid}/…/
```

مثال: `/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa/bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb/`

- العمق = عدد مقاطع الـGUID (حد الكتابة: **32** عبر `OrgUnitPathService.MaxDepth`).
- لا جدول closure في PERF-003.

## الاستعلامات

| عملية | الطريقة |
|---|---|
| Subtree / Org scope | `MaterializedPath.StartsWith(rootPath)` + فلتر `TenantId` |
| إنشاء | `AssignPathOnCreateAsync` بعد حفظ الـId |
| نقل | `MoveSubtreeAsync` — معاملة واحدة تعيد كتابة بادئة المسار للفرع فقط |
| إصلاح كامل | `RecomputeAllAsync` (startup/admin فقط) |

فهارس: `IX_OrgUnits_MaterializedPath`، `IX_OrgUnits_TenantId_MaterializedPath`؛ على الـsnapshots: `ParentId` و`(TenantId, MaterializedPath)`.

## منع الدورات

- رفض `ParentId == Id`
- رفض إن مسار الأب يحتوي `/{unitId}/` (نقل تحت النسل)

## الكاش

- `IOrgUnitHierarchyCache` (`IMemoryCache`) بمفاتيح `org-scope:{tenantId}:{rootId}`
- TTL ≈ 90 ثانية
- `InvalidateTenant` عند Create/Edit/Delete/Move في HR.Core
- عزل حسب `TenantId`؛ لا Redis في PERF-003 — النشر متعدد النسخ يحتاج لاحقاً إبطال موزّعاً (OPS/NFR)

## النطاق (IAM)

- بلا claim فرع → `Unrestricted` دون تحميل الشجرة
- claim غير صالح / جذر مفقود → `Denied`
- claim صالح → مجموعة Ids عبر path prefix (مع كاش)

## واجهة الشجرة

- Tree / Lookup `org-units/tree`: تحميل عبر path prefix مع سقف `MaxTreeNodes = 500`

## التحقق

اختبارات: `Perf003OrgUnitHierarchyTests` (دورات، نقل، scope، عزل كاش).
