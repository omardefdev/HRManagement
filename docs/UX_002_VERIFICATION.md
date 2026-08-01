# UX-002 — حزمة التحقق

| تحقق | دليل | نتيجة |
|---|---|---|
| ADR + جرد | [ADR-UX-002-a11y.md](./ADR-UX-002-a11y.md), [UX_A11Y_INVENTORY.md](./UX_A11Y_INVENTORY.md) | Ready |
| Skip / landmarks / chrome names | `_HrSharedLayout`, Gateway `_Layout`, `_PublicLayout` | OK |
| Focus-visible + print CSS | `syrian-theme.css` | OK |
| Modals / spinner SR | `_DeleteConfirmation`, `_OrgUnitTreeModal` | OK |
| Search label + a11y forms JS | `_HrSearchBar`, `a11y-forms.js` | OK |
| جداول متجاوبة | CSS `:has()` + `table-responsive` على Indexes حرجة | OK |
| أسماء أزرار الأيقونة | `fix-icon-button-aria.py` + `check-icon-button-names.py` في CI | OK |
| axe serious/critical | `A11yAxeE2ETests` + `testdata/e2e/axe.min.js` (يتطلب `RUN_PLAYWRIGHT_E2E=true`) | OK |
| لوحة مفاتيح (يدوي) | Tab → skip → محتوى؛ Enter على أزرار الشجرة؛ إغلاق modal بـEsc | قائمة تحقق |
| قارئ شاشة (يدوي) | NVDA/Narrator على Employees Index + Create | موثّق |

## قائمة تدقيق لوحة مفاتيح

1. تفعيل skip-link بـTab والانتقال إلى `#main-content`.
2. التنقل في القائمة الجانبية/البوابة بدون فأرة.
3. فتح/إغلاق modal الحذف بلوحة المفاتيح.
4. اختيار وحدة من شجرة OrgUnit بـEnter/Space.

## خارج النطاق

L10N-001/002، MOB-001، NAT-005.

## إغلاق

**UX-002 = Done** مع تحديث TRACEABILITY / CI_AND_TESTING.
