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

### PLAN-67 — one zone, one format, everywhere (D-67-2)

- **The zone** is `Localization:TimeZone` (default `Asia/Damascus`; on Windows the IANA id resolves through ICU, with `Syria Standard Time` as the fallback). `DamascusCalendarService` (a singleton, resolved at start-up by `UseHrLocalization`) reads it and sets `HrLocalTime.Zone`, which code outside a view uses (the candidate portal, its messages, generated documents). An unknown id stops the service at start-up, with a message that names the key.
- **Storage is unchanged:** every time is stored in UTC. Only what the user sees and types changes.
- **Formats** (`HrLocalTime`): a date `yyyy/MM/dd`, a time `yyyy/MM/dd HH:mm`, a time of day `HH:mm`; the hint «بتوقيت دمشق».
- **In a view** (`HRManagement.Web.Shared.Html.HrTimeHtmlExtensions`):
  - `Html.HrDate(value)`: a calendar date (`DateOnly` is formatted, never converted; a `DateTime` is converted to the zone first);
  - `Html.HrDateTime(value)`: `<time datetime="<UTC ISO>" title="بتوقيت دمشق" dir="ltr">yyyy/MM/dd HH:mm</time>`, so a test can compare what is shown with what is stored;
  - `Html.HrTime(value)`: a time of day;
  - `HrDateText` / `HrDateTimeText`: the same as plain strings, for attributes and joined text;
  - a missing value prints «—».
- **Input:** `<input type="datetime-local" asp-for="…">` shows the stored UTC value on the zone's clock (`HrDateTimeLocalTagHelper`), and `HrLocalDateTimeModelBinder` binds what the user typed back to UTC, once. A date alone, or a value that carries `Z` or an offset, is bound as before; `[ApiController]` bodies are never touched. Controllers that take a time as a string use `HrLocalTime.TryParseUserTime`.
- **A calendar date is not a time:** a property that means a date (a payroll period, an effective date, a loan's due date) carries `[DataType(DataType.Date)]` and is entered with `type="date"`; the binder leaves it as posted. Read as a Damascus time, 1 December would be stored as 30 November 21:00 UTC and its date would move back a day. The guard's `date-as-time` rule refuses a `datetime-local` input bound to such a property, or to one named `…Date`.
- **A skipped hour:** Damascus kept summer time until 2022. A time typed inside an hour the clocks skipped never existed; it is read as the same time after the change rather than failing the request.
- **The guard:** `InterfaceTextGuardTests` refuses a view that formats a date or time outside these helpers (`unformatted-time`).

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
