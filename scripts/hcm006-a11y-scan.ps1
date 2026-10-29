# HCM-006: baseline a11y checks for public careers views.
# HCM-030 (PLAN-30 K4): extended to the candidate portal (/careers/my login, code, page with the offer, withdraw, appeal and
# document forms), the apply form with uploads and consent, and the track pages. Static text scan (no Razor rendering):
#   - every <label for="X"> has an element id="X", and every visible form control has a label (or aria-label/labelledby);
#   - every field error (class invalid-feedback, or @Html.ValidationMessage) has an id that an aria-describedby references,
#     and a control that references an error carries aria-invalid;
#   - the one-time-code input has autocomplete="one-time-code" and inputmode="numeric";
#   - a view with form controls has a focus target after errors: an error summary with data-hr-error-summary, role="alert"
#     and tabindex="-1", focused by ~/js/careers-portal.js, with a visible focus style in _PublicLayout;
#   - nonce CSP: no inline style attributes or inline event handlers, no native confirm(); decorative icons aria-hidden.
# Ids and for= values are compared as literal text, with Razor expressions normalized (@x, @(x) and {x} are the same).
# "No critical findings" = exit 0 (every check is blocking).
param([string]$RepoRoot = "")

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Resolve-RepoPath([string]$rel) {
    return (Join-Path $RepoRoot ($rel -replace '/', [IO.Path]::DirectorySeparatorChar))
}

# Razor expressions in ids: "@inputId", "@(code)" and the interpolation "{inputId}" all become "{inputId}".
function Normalize-Razor([string]$text) {
    $text = [regex]::Replace($text, '@\(([A-Za-z_][\w\.]*)\)', '{$1}')
    return [regex]::Replace($text, '@([A-Za-z_][\w\.]*)', '{$1}')
}

function Get-Attr([string]$tag, [string]$name) {
    $m = [regex]::Match($tag, '\s' + [regex]::Escape($name) + '="([^"]*)"')
    if ($m.Success) { return $m.Groups[1].Value }
    return $null
}

$viewsRoot = "src/Services/HRCore/HR.Core.Web/Views"
$baselineViews = @(
    "$viewsRoot/PublicCareers/Details.cshtml",
    "$viewsRoot/PublicCareers/Track.cshtml",
    "$viewsRoot/PublicCareers/TrackResult.cshtml",
    "$viewsRoot/Shared/_PublicLayout.cshtml",
    "$viewsRoot/CandidatePortal/Login.cshtml",
    "$viewsRoot/CandidatePortal/Code.cshtml",
    "$viewsRoot/CandidatePortal/Index.cshtml"
)
$views = $baselineViews + @(
    "$viewsRoot/PublicCareers/Index.cshtml",
    "$viewsRoot/PublicCareers/Submitted.cshtml",
    "$viewsRoot/PublicCareers/Privacy.cshtml"
)
$otpView = "$viewsRoot/CandidatePortal/Code.cshtml"
$focusScript = "src/Services/HRCore/HR.Core.Web/wwwroot/js/careers-portal.js"

$failures = @()
foreach ($rel in $views) {
    $path = Resolve-RepoPath $rel
    if (-not (Test-Path $path)) {
        $failures += "missing $rel"
        continue
    }
    $content = Get-Content -Raw -Encoding UTF8 -Path $path
    $norm = Normalize-Razor $content

    if ($baselineViews -contains $rel -and $content -notmatch 'aria-label|for="') {
        $failures += "$rel missing aria-label or label/for pairing"
    }

    # Nonce CSP and UX-003.
    if ($content -match '\sstyle\s*=\s*"') { $failures += "$rel has an inline style attribute (blocked by the nonce CSP)" }
    if ($content -match '\son[a-z]+\s*=\s*"') { $failures += "$rel has an inline event handler (blocked by the nonce CSP)" }
    if ($content -match '\bconfirm\s*\(') { $failures += "$rel uses native confirm()" }

    # Decorative Font Awesome icons are hidden from assistive technology.
    foreach ($icon in [regex]::Matches($content, '<i\s[^<>]*class="[^"]*\bfa[srb]?\b[^"]*"[^<>]*>')) {
        if ($icon.Value -notmatch 'aria-hidden="true"') {
            $failures += "$rel icon without aria-hidden: $($icon.Value)"
        }
    }

    # Ids present in the view (normalized).
    $ids = @{}
    foreach ($m in [regex]::Matches($norm, '\sid="([^"]+)"')) { $ids[$m.Groups[1].Value] = $true }

    # Label/for pairs.
    $labelled = @{}
    foreach ($m in [regex]::Matches($norm, '<label\b[^<>]*\sfor="([^"]+)"')) {
        $target = $m.Groups[1].Value
        $labelled[$target] = $true
        if (-not $ids.ContainsKey($target)) { $failures += "$rel label for=""$target"" has no element with that id" }
    }

    # Form controls: label, aria-invalid when an error is referenced, one-time-code input.
    $visibleControls = 0
    foreach ($m in [regex]::Matches($norm, '<(input|select|textarea)\b[^<>]*>')) {
        $tag = $m.Value
        $type = Get-Attr $tag 'type'
        if ($type -in @('hidden', 'submit', 'button', 'reset', 'image')) { continue }
        $visibleControls++
        $id = Get-Attr $tag 'id'
        $name = Get-Attr $tag 'name'
        if (-not ($id -and $labelled.ContainsKey($id)) -and $tag -notmatch '\saria-label(ledby)?="') {
            $failures += "$rel control name=""$name"" has no <label for> or aria-label"
        }
        if ($tag -match 'aria-describedby=[^<>]*-error' -and $tag -notmatch '\saria-invalid="') {
            $failures += "$rel control name=""$name"" references an error without aria-invalid"
        }
        if ($name -eq 'Code' -or $name -match '(?i)otp') {
            if ($tag -notmatch '\sautocomplete="one-time-code"') { $failures += "$rel one-time-code input missing autocomplete=""one-time-code""" }
            if ($tag -notmatch '\sinputmode="numeric"') { $failures += "$rel one-time-code input missing inputmode=""numeric""" }
        }
    }
    if ($rel -eq $otpView -and $norm -notmatch '<input\b[^<>]*\sname="Code"[^<>]*autocomplete="one-time-code"') {
        $failures += "$rel has no one-time-code input"
    }

    # Field errors are announced with their field.
    foreach ($m in [regex]::Matches($norm, '<[a-z]+\b[^<>]*class="[^"]*\binvalid-feedback\b[^"]*"[^<>]*>')) {
        $errorId = Get-Attr $m.Value 'id'
        if (-not $errorId) {
            $failures += "$rel error message without id: $($m.Value)"
        } elseif ($norm -notmatch ('aria-describedby=[^<>]*' + [regex]::Escape($errorId))) {
            $failures += "$rel error id ""$errorId"" is not referenced by any aria-describedby"
        }
    }
    foreach ($m in [regex]::Matches($content, '@Html\.ValidationMessage\([^\r\n]*')) {
        $idMatch = [regex]::Match($m.Value, '\bid\s*=\s*"([^"]+)"')
        if (-not $idMatch.Success) {
            $failures += "$rel ValidationMessage without a literal id: $($m.Value.Trim())"
        } elseif ($norm -notmatch ('aria-describedby=[^<>]*' + [regex]::Escape($idMatch.Groups[1].Value))) {
            $failures += "$rel error id ""$($idMatch.Groups[1].Value)"" is not referenced by any aria-describedby"
        }
    }

    # Focus target after errors.
    if ($visibleControls -gt 0 -and $rel -notlike '*/_PublicLayout.cshtml') {
        $summaries = [regex]::Matches($norm, '<[a-z]+\b[^<>]*\bdata-hr-error-summary\b[^<>]*>')
        if ($summaries.Count -eq 0) {
            $failures += "$rel has form controls but no error summary (data-hr-error-summary) to focus after errors"
        }
        foreach ($s in $summaries) {
            if ($s.Value -notmatch 'role="alert"' -or $s.Value -notmatch 'tabindex="-1"') {
                $failures += "$rel error summary needs role=""alert"" and tabindex=""-1"": $($s.Value)"
            }
        }
        if ($content -notmatch 'careers-portal\.js') {
            $failures += "$rel does not load ~/js/careers-portal.js (focus on the error summary)"
        }
    }
}

$layout = Resolve-RepoPath "$viewsRoot/Shared/_PublicLayout.cshtml"
if (Test-Path $layout) {
    $layoutContent = Get-Content -Raw -Encoding UTF8 -Path $layout
    if ($layoutContent -notmatch 'hr-skip-link') { $failures += "_PublicLayout missing skip link" }
    if ($layoutContent -notmatch 'id="main-content"') { $failures += "_PublicLayout missing main landmark" }
    if ($layoutContent -notmatch '\[data-hr-error-summary\]:focus[^{]*\{[^}]*outline:\s*[1-9]') {
        $failures += "_PublicLayout missing a visible focus style for [data-hr-error-summary]"
    }
}

$scriptPath = Resolve-RepoPath $focusScript
if (-not (Test-Path $scriptPath)) {
    $failures += "missing $focusScript"
} else {
    $scriptContent = Get-Content -Raw -Encoding UTF8 -Path $scriptPath
    if ($scriptContent -notmatch 'data-hr-error-summary' -or $scriptContent -notmatch '\.focus\(\)') {
        $failures += "$focusScript does not focus [data-hr-error-summary]"
    }
}

if ($failures.Count -gt 0) {
    Write-Host "FAIL"
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host "PASS: HCM-006/HCM-030 public careers and candidate portal a11y scan ($($views.Count) views)"
exit 0
