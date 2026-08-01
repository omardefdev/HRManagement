# Repairs broken select/textarea markup from add-form-labels.ps1.
param(
    [string]$Root = (Join-Path $PSScriptRoot "..")
)

$viewFiles = Get-ChildItem -Path (Join-Path $Root "src") -Recurse -Include "Create.cshtml", "Edit.cshtml" |
    Where-Object { $_.FullName -match "\\Views\\" }

$fixed = 0
foreach ($file in $viewFiles) {
    $content = [IO.File]::ReadAllText($file.FullName)
    $original = $content

    # textarea: validation/div inside element.
    $content = [regex]::Replace(
        $content,
        '<textarea([^>]*)><span asp-validation-for="([^"]+)" class="text-danger"></span></div></textarea>',
        '<textarea$1></textarea><span asp-validation-for="$2" class="text-danger"></span></div>')

    # select: validation/div inside element (supports inner <option> tags).
    $content = [regex]::Replace(
        $content,
        '<select([^>]*)><span asp-validation-for="([^"]+)" class="text-danger"></span></div>(.*?)</select></div>',
        '<select$1>$3</select><span asp-validation-for="$2" class="text-danger"></span></div>',
        [Text.RegularExpressions.RegexOptions]::Singleline)

    # enum select broken before ()">
    $content = [regex]::Replace(
        $content,
        'GetEnumSelectList<([^>]+)><span asp-validation-for="([^"]+)" class="text-danger"></span></div>\(\)"></select>',
        'GetEnumSelectList<$1>()"></select><span asp-validation-for="$2" class="text-danger"></span></div>')

    $content = [regex]::Replace(
        $content,
        'GetEnumSelectList<([^>]+)><span asp-validation-for="([^"]+)" class="text-danger"></span></div>\(\)" class="form-control[^"]*"></select>',
        'GetEnumSelectList<$1>()" class="form-control"></select><span asp-validation-for="$2" class="text-danger"></span></div>')

    # select: option placed after accidental closing div.
    $content = [regex]::Replace(
        $content,
        '<select([^>]*)><span asp-validation-for="([^"]+)" class="text-danger"></span></div>(<option[^>]*>[^<]*</option>)</select>',
        '<select$1>$3</select><span asp-validation-for="$2" class="text-danger"></span></div>')

    # Duplicate class attributes on selects.
    $content = [regex]::Replace(
        $content,
        '(<select[^>]*class="form-control")([^>]*?)class="form-control"',
        '$1$2')

    # Nested mb-2 > mb-3 wrappers.
    $content = $content -replace '<div class="mb-2"><div class="mb-3">', '<div class="mb-3">'
    while ($content -match '</div></div>(\s*<div class="mb-3">)') {
        $content = $content -replace '</div></div>(\s*<div class="mb-3">)', '</div>$1'
    }

    if ($content -ne $original) {
        [IO.File]::WriteAllText($file.FullName, $content)
        $fixed++
        Write-Host "[repaired] $($file.FullName)"
    }
}

Write-Host "Repaired files: $fixed"
