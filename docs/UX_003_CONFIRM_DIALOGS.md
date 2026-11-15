# UX-003 — ديالوغات التأكيد الموحّدة

## الهدف

استبدال `window.confirm()` / `window.alert()` بديالوغ Bootstrap متوافق مع ثيم النظام (`syrian-theme.css`).

## المكوّنات

| مكوّن | مسار |
|--------|------|
| Modal HTML | [`_HrConfirmModal.cshtml`](../src/Shared/HRManagement.Web.Shared/Views/Shared/_HrConfirmModal.cshtml) |
| JavaScript API | [`shared-ui.js`](../src/Shared/HRManagement.Web.Shared/wwwroot/js/shared-ui.js) — `hrSharedUi.confirm()` / `hrSharedUi.alert()` |
| CSS | [`syrian-theme.css`](../src/Shared/HRManagement.Web.Shared/wwwroot/css/syrian-theme.css) — `.hr-confirm-modal` |
| Layout | [`_HrSharedLayout.cshtml`](../src/Shared/HRManagement.Web.Shared/Views/Shared/_HrSharedLayout.cshtml) |

## الاستخدام في Views

```html
<form method="post"
      data-hr-confirm="هل أنت متأكد؟"
      data-hr-confirm-title="تأكيد العملية"
      data-hr-confirm-variant="warning"
      data-hr-confirm-label="تأكيد">
```

**Variants:** `warning` (افتراضي) | `danger` (حذف/إلغاء) | `info` (تنبيه)

## Legacy delete (Index pages)

أزرار `data-delete-url` + `data-bs-target="#deleteModal"` تعمل عبر adapter في `shared-ui.js` وتُرسل `#hrConfirmDeleteForm`.

## التحقق

```powershell
./scripts/ux003-no-native-confirm.ps1
```

## قائمة UAT

1. Assets → تقرير فقدان → modal بثيم burgundy/gold
2. حذف سجل → variant danger + زر «حذف»
3. Esc / إلغاء → لا يُنفَّذ الإجراء
4. Tab → focus على زر الإلغاء
5. Index مع `data-delete-url` → حذف يعمل
6. Assets → تسليم/استرداد/فقدان → **خطأ التحقق (client أو server)** = `#hrConfirmModal` عبر `hrSharedUi.alert` وليس banner أحمر في `_HrAlerts`
7. بعد إغلاق ديالوغ الخطأ → scroll تلقائي إلى النموذج المعني + إعادة تعبئة المسودة (draft)

## أخطاء النماذج (عهد / فقدان)

| الطبقة | السلوك |
|--------|--------|
| Client (`validateAssetDetailForm`) | `hrSharedUi.alert({ variant: 'danger' })` |
| Server (`SetFormError` / `SetDialogError`) | `TempData["ShowDialog"]=true` → `#hrFlashDialog` في Layout |
| `_HrAlerts` | لا يعرض banner `Error` عندما `ShowDialog=true` (تجنب التكرار) |

**ملاحظة:** بعد نشر UX-003، أعد build خدمة `admin-logistics-web` (صورة Docker قديمة قد تعرض `confirm()` native).

## العلاقة بـ UX-002

- modal واحد في Layout
- `aria-labelledby` + `btn-close` + focus trap
- لا `confirm()` native في Views

## PLAN-67 — لا `confirm()` مضمّن متبقٍّ

- **ما بقي قبل PLAN-67:** عشرة أزرار ونماذج تستدعي `confirm()` في معالج `onclick` أو `onsubmit` مضمّن. تحت سياسة CSP نافذة لا يعمل المعالج المضمّن، فكان الإجراء الهدّام يُنفَّذ بلا سؤال.
- **الآن:** كل واحد منها `data-hr-confirm` على نموذجه بالنص العربي نفسه، و`data-hr-confirm-variant="danger"` للهدّام. يفتح `shared-ui.js` الديالوغ `#hrConfirmModal`، والإلغاء لا يرسل شيئاً.
- **صفحة مراجعة الاعتماد:** نموذج واحد يحمل الملاحظات والقرارين؛ «رفض» يرسل إلى إجرائه عبر `formaction` على الزر، فتصل ملاحظات المراجع إلى السجل في الحالتين.
- **الفحص:** `scripts/ux003-no-native-confirm.ps1`، و`scripts/ux067-validate.ps1` (لا `on…=` في أي عرض)، و`InterfaceTextGuardTests`، وAC-UX-67a: حذف مسودة طلب شاغر يسأل أولاً، والإلغاء لا يغيّر شيئاً، والتأكيد يحذفها؛ وملاحظات الرفض تصل السجل.
- **نماذج هدّامة كانت بلا سؤال** (وجدها التحقق في P7): أربعة عشر نموذجاً في سبع خدمات كانت تُرسل فوراً، منها «إهمال المسودة» في محرر قواعد السياسة، وسحب الإجراء التأديبي وطلب البطاقة وطلب المرشح، وإلغاء الإجازة وطلب الاعتماد والمقابلة ومهمة الرواتب وطلب الإتلاف، وإبطال التفويض والعرض والصلاحية، وحذف القاعدة وسطر الخطة. كلها الآن `data-hr-confirm` بالمتغير `danger`، وتخطيط البوابة العامة يحمل الديالوغ و`shared-ui.js` لسحب طلب المرشح. وقاعدة الحارس `unconfirmed-destructive` ترفض نموذجاً هدّاماً بلا تأكيد.
