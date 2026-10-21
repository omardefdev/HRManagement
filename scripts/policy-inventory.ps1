# Lists static *Rules.cs for policy migration tracking
$root = Split-Path $PSScriptRoot -Parent
$files = Get-ChildItem -Path (Join-Path $root "src\Services") -Recurse -Filter "*Rules.cs" |
    Where-Object { $_.FullName -notmatch "\\obj\\|\\bin\\" }
$rows = foreach ($f in $files) {
    [PSCustomObject]@{
        File = $f.FullName.Replace($root + "\", "")
        Service = ($f.FullName -split "\\Services\\")[1].Split("\\")[0]
    }
}
$out = Join-Path $root "docs\policy-rules-inventory.csv"
$rows | Export-Csv -Path $out -NoTypeInformation -Encoding UTF8
Write-Host "Wrote $($rows.Count) entries to $out"
