# Shared Web Assets Policy

## Shared source of truth

Use `src/Shared/HRManagement.Web.Shared` as the single source for reusable visual assets:

- `wwwroot/css/syrian-theme.css`
- `wwwroot/css/theme-tokens.css`
- `wwwroot/fonts/*`
- `wwwroot/js/shared-ui.js`
- `wwwroot/images/brand/*`
- `Views/Shared/*` for reusable partials

## Consumption rules

- All web projects reference `HRManagement.Web.Shared`.
- Shared stylesheet path remains `~/css/syrian-theme.css`.
- Service-specific styles remain in each service `wwwroot/css/site.css`.
- Service-specific JS and images stay local to the service.

## Font files

Place the following font files in:
`src/Shared/HRManagement.Web.Shared/wwwroot/fonts/`

- `itfQomraArabic-Light.woff`
- `itfQomraArabic-Light.ttf`
- `itfQomraArabic-Black.ttf`

Until the files are added, the theme falls back to locally installed Qomra font names where available.
