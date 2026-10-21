# L10N-003 — Arabic frontend localization

**Card:** L10N-003  
**Scope:** Enum display helpers, stored status translation, bilingual reference data, jQuery validation Arabic, SharedResources expansion, module wiring.

## Key components

| Component | Location |
|-----------|----------|
| `EnumDisplayExtensions` | `HRManagement.SharedKernel/Localization/` |
| `ILocalizedName` / `LocalizedNameExtensions` | `HRManagement.SharedKernel/Localization/` |
| `HrEnumDisplayTagHelper` / `HrStoredEnumTagHelper` | `HRManagement.Web.Shared/TagHelpers/` |
| `HrDisplayHtmlExtensions` | `HRManagement.Web.Shared/Html/` |
| Arabic jQuery Validate | `wwwroot/js/jquery-validate-ar.js` |
| Shared UI strings | `HRManagement.Web.Shared/Resources/SharedResources.resx` |

## Usage in Razor

```cshtml
@Html.HrEnumDisplay(Model.Status)
@Html.HrStoredEnumDisplay("EmploymentStatus", h.NewStatus)
<span hr-enum-display="@Model.Status"></span>
@Model.GetLocalizedName()  @* ILocalizedName entities *@
```

## Validation

```powershell
powershell -File scripts/l10n003-arabic-ui-scan.ps1
dotnet test --filter L10n003ArabicUiTests
```

## Notes

- Enum members use `[Display(Name = "عربي")]` across Domain/SharedKernel enums.
- Stored history keeps English enum keys in DB; translation happens at display time.
- All 14 module web apps + Gateway call `AddHrLocalization()` / `UseHrLocalization()`.
