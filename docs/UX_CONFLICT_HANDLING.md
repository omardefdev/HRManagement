# UX-004 — Conflict / duplicate error handling

When a save violates a unique constraint or optimistic concurrency token, users must **never** see a raw JSON page in the browser.

## Layers

1. **Controller** — prefer `HrSaveChanges.TryAsync` + pre-checks before `SaveChanges`.
2. **Middleware** — `ConcurrencyConflictMiddleware` redirects HTML forms with `TempData["Error"]` + `TempData["ShowDialog"]`; API/AJAX still get HTTP 409 JSON.
3. **Client** — `hrSharedUi.alert` via flash hook (`#hrFlashDialog`) or fetch 409 interceptor.

## Controller pattern (Create/Edit)

```csharp
await ValidateUniqueTagNumberAsync(entity.TagNumber, excludeId: id);
if (!ModelState.IsValid) {
    ViewData["ShowValidationDialog"] = true;
    return View(entity);
}

var outcome = await HrSaveChanges.TryAsync(db, ModelState, ct);
if (outcome != SaveChangesOutcome.Success) {
    ViewData["ShowValidationDialog"] = true;
    return View(entity);
}
```

Include in the view Scripts section:

```cshtml
@await Html.PartialAsync("_ValidationDialogTrigger")
```

## HTML form POST rules

- Do **not** `return BadRequest()` for browser form posts.
- Use `TempData["Error"]` + `TempData["ShowDialog"] = "true"` + `RedirectToAction`, or `return View(model)` with `ModelState`.

## Adding a new unique constraint message

Edit [`UniqueConstraintMessageRegistry.cs`](../src/Shared/HRManagement.ServiceDefaults/UniqueConstraintMessageRegistry.cs):

```csharp
["IX_YourEntity_TenantId_Code"] = new("رسالة عربية", "PropertyName"),
```

Run `scripts/ux-conflict-scan.ps1` in CI to flag `[HttpPost]` actions that return `BadRequest()` without `return View`.

## Related files

- [`ConcurrencyConflictMiddleware.cs`](../src/Shared/HRManagement.ServiceDefaults/ConcurrencyConflictMiddleware.cs)
- [`HrRequestClassification.cs`](../src/Shared/HRManagement.ServiceDefaults/HrRequestClassification.cs)
- [`HrSaveChanges.cs`](../src/Shared/HRManagement.ServiceDefaults/Mvc/HrSaveChanges.cs)
- [`shared-ui.js`](../src/Shared/HRManagement.Web.Shared/wwwroot/js/shared-ui.js)
