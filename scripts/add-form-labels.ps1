# Adds missing <label asp-for> to Create/Edit form views.
param(
    [string]$Root = (Join-Path $PSScriptRoot ".."),
    [switch]$WhatIf
)

$viewFiles = Get-ChildItem -Path (Join-Path $Root "src") -Recurse -Include "Create.cshtml", "Edit.cshtml" |
    Where-Object { $_.FullName -match "\\Views\\" }

function Get-LabeledProperties([string]$content) {
    [regex]::Matches($content, '<label[^>]*\basp-for="([^"]+)"') |
        ForEach-Object { $_.Groups[1].Value } |
        Select-Object -Unique
}

function Normalize-Control([string]$tag, [bool]$isCheck) {
    $tag = $tag -replace '\s+mb-\d+', ''
    if ($isCheck) {
        if ($tag -notmatch '\bclass="') {
            return $tag -replace '<(input|select|textarea)\b', '<$1 class="form-check-input"'
        }
        if ($tag -notmatch 'form-check-input') {
            return $tag -replace '\bclass="', 'class="form-check-input '
        }
        return $tag
    }

    if ($tag -notmatch '\bclass="') {
        return $tag -replace '<(input|select|textarea)\b', '<$1 class="form-control"'
    }
    if ($tag -notmatch 'form-control') {
        return $tag -replace '\bclass="', 'class="form-control '
    }
    return $tag
}

function Wrap-WithLabel([string]$control, [string]$prop) {
    $isCheck = $control -match 'form-check-input|type\s*=\s*"(checkbox|radio)"'
    $control = Normalize-Control $control $isCheck
    if ($isCheck) {
        return "<div class=""form-check mb-3"">$control<label asp-for=""$prop"" class=""form-check-label""></label><span asp-validation-for=""$prop"" class=""text-danger""></span></div>"
    }
    return "<div class=""mb-3""><label asp-for=""$prop"" class=""form-label""></label>$control<span asp-validation-for=""$prop"" class=""text-danger""></span></div>"
}

$changed = 0
foreach ($file in $viewFiles) {
    $original = [IO.File]::ReadAllText($file.FullName)
    $content = $original

    # Manual labels: <label class="form-label">...</label><control asp-for="Prop">
    $manualMatches = [regex]::Matches(
        $content,
        '<label\s+class="form-label"[^>]*>[^<]*</label>\s*(<(input|select|textarea)\b[^>]*\basp-for="([^"]+)"[^>]*>)',
        [Text.RegularExpressions.RegexOptions]::IgnoreCase)

    for ($i = $manualMatches.Count - 1; $i -ge 0; $i--) {
        $m = $manualMatches[$i]
        $prop = $m.Groups[3].Value
        $control = $m.Groups[1].Value
        $wrapped = Wrap-WithLabel $control $prop
        $content = $content.Remove($m.Index, $m.Length).Insert($m.Index, $wrapped)
    }

    # Ensure asp-for labels use form-label / form-check-label.
    $content = [regex]::Replace($content, '<label\s+asp-for="([^"]+)"\s*class="form-check-label"\s*>', '<label asp-for="$1" class="form-check-label">')
    $content = [regex]::Replace($content, '<label\s+asp-for="([^"]+)"\s*class="form-label"\s*>', '<label asp-for="$1" class="form-label">')
    $content = [regex]::Replace($content, '<label\s+asp-for="([^"]+)"\s*>', '<label asp-for="$1" class="form-label">')

    $labeled = @(Get-LabeledProperties $content)
    $matches = [regex]::Matches(
        $content,
        '<(input|select|textarea)\b[^>]*\basp-for="([^"]+)"[^>]*>',
        [Text.RegularExpressions.RegexOptions]::IgnoreCase)

    for ($i = $matches.Count - 1; $i -ge 0; $i--) {
        $m = $matches[$i]
        if ($m.Groups.Count -lt 3) { continue }
        $prop = $m.Groups[2].Value
        if ([string]::IsNullOrWhiteSpace($prop)) { continue }
        $full = $m.Value
        if ($full -match 'type\s*=\s*"hidden"') { continue }
        if ($labeled -contains $prop) { continue }

        # Skip controls already inside a labeled form group.
        $before = $content.Substring(0, $m.Index)
        if ($before -match '<label[^>]*\basp-for="' + [regex]::Escape($prop) + '"[^>]*>\s*$') { continue }

        $wrapped = Wrap-WithLabel $full $prop
        $content = $content.Remove($m.Index, $m.Length).Insert($m.Index, $wrapped)
        $labeled += $prop
    }

    if ($content -ne $original) {
        $changed++
        if ($WhatIf) {
            Write-Host "[would change] $($file.FullName)"
        } else {
            [IO.File]::WriteAllText($file.FullName, $content)
            Write-Host "[updated] $($file.FullName)"
        }
    }
}

Write-Host "Done. Files changed: $changed / $($viewFiles.Count)"
