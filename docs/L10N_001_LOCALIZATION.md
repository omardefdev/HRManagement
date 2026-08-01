# L10N-001 — Localization foundation

**Card:** L10N-001 (P1-National)  
**Status:** Done  
**Scope:** Shared localization infrastructure, ar-SY default, en fallback, Damascus calendar helpers, Hijri conversion, language toggle endpoint.

## Architecture

| Component | Location |
|-----------|----------|
| `AddHrLocalization()` / `UseHrLocalization()` | `HRManagement.ServiceDefaults/Localization/HrLocalizationExtensions.cs` |
| Calendar service | `ICalendarService`, `DamascusCalendarService` (Asia/Damascus, Gregorian + Hijri) |
| Shared strings (ServiceDefaults) | `Localization/Resources/SharedResources.{ar,en}.resx` |
| Shared strings (Web.Shared) | `Resources/SharedResources.resx` (ar default) + `.en.resx` |
| Marker class (ServiceDefaults) | `Localization/Resources/SharedResources.cs` |
| Marker class (Web.Shared) | `Resources/SharedResources.cs` |
| Language toggle | `Localization/CultureController.cs` — `POST /api/culture?culture=ar-SY` |

## Supported cultures

- **Default:** `ar-SY` (Arabic — Syria)
- **Fallback:** `en`

Culture resolution order:

1. Query string (`?culture=`, `?ui-culture=`)
2. Cookie `.HrManagement.Culture`
3. `Accept-Language` header

## Wiring

Gateway and HR.Core call:

```csharp
builder.AddHrLocalization();
// ...
app.UseHrLocalization();
```

Other modules should adopt the same pattern when user-facing strings migrate off hardcoded text. The `CultureController` is automatically discovered when controllers are mapped (e.g. `app.MapControllers()`).

## Shared resources

Common labels (Save, Cancel, Search, Home, Employees, Payroll, Leave, etc.) live in `SharedResources` resx files. Inject via:

```csharp
using HRManagement.ServiceDefaults.Localization.Resources;

IStringLocalizer<SharedResources> localizer
```

For Web.Shared (Razor views):

```csharp
using HRManagement.Web.Shared.Resources;

IStringLocalizer<SharedResources> localizer
```

### Resource keys

| Key | Arabic | English |
|-----|--------|---------|
| Save | حفظ | Save |
| Cancel | إلغاء | Cancel |
| Search | بحث | Search |
| Login | تسجيل الدخول | Log in |
| Logout | تسجيل الخروج | Log out |
| Home | الرئيسية | Home |
| Employees | الموظفون | Employees |
| Payroll | الرواتب | Payroll |
| Leave | الإجازات | Leave |
| Dashboard | لوحة التحكم | Dashboard |
| Settings | الإعدادات | Settings |
| Reports | التقارير | Reports |
| Contracts | العقود | Contracts |
| Training | التدريب | Training |
| Archive | الأرشيف | Archive |
| Confirm | تأكيد | Confirm |
| Delete | حذف | Delete |
| Edit | تعديل | Edit |
| Add | إضافة | Add |
| Loading | جاري التحميل... | Loading... |
| NoResults | لا توجد نتائج | No results found |

## Calendar

`DamascusCalendarService` converts UTC to `Asia/Damascus` and formats Gregorian dates per culture.

### Hijri calendar

`FormatHijriDate` uses `System.Globalization.HijriCalendar` to convert Gregorian dates to Hijri (Islamic calendar) in `YYYY/MM/DD` format. Returns `null` if the date is outside the supported Hijri range.

```csharp
ICalendarService calendar = ...;
string? hijri = calendar.FormatHijriDate(DateTime.Today);
// e.g. "1448/01/06"
```

## Language toggle

`CultureController` provides:

- **`POST /api/culture?culture=en&returnUrl=/`** — sets the `.HrManagement.Culture` cookie and redirects (or returns JSON).
- **`GET /api/culture`** — returns current culture, UI culture, and supported cultures.

To add a language toggle in UI views:

```html
<form method="post" action="/api/culture?culture=en&returnUrl=@Context.Request.Path">
  <button type="submit">English</button>
</form>
```

## Validation

```powershell
powershell -File scripts/l10n001-validate.ps1
dotnet test --filter L10n001LocalizationTests
```

## Out of scope (later cards)

- L10N-002 — Arabic normalization and collation
- Full Hijri calendar as primary display calendar
- Pseudo-localization CI gate
- PDF/email template localization
